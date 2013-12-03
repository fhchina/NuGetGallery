﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NuGet.Services.Jobs.Monitoring;
using System.Diagnostics;
using System.Data.SqlClient;

namespace NuGet.Services.Jobs
{
    public abstract class JobBase
    {
        public InvocationContext Context { get; protected set; }

        public Invocation Invocation { get { return Context.Invocation; } }
        public InvocationRequest Request { get { return Context.Request; } }
        public ServiceConfiguration Config { get { return Context.Config; } }

        public virtual async Task<InvocationResult> Invoke(InvocationContext context)
        {
            InvocationResult result = BindContext(context);
            if (result != null)
            {
                return result;
            }

            // Invoke the job
            try
            {
                result = await Invoke();
            }
            catch (Exception ex)
            {
                result = InvocationResult.Faulted(ex);
            }

            // Return the result
            return result;
        }

        public abstract EventSource GetEventSource();
        protected internal abstract Task<InvocationResult> Invoke();

        protected virtual Task Enqueue(Invocation request)
        {
            return Context.Queue.Enqueue(request);
        }

        protected virtual Task Extend(TimeSpan duration)
        {
            return Context.Queue.Extend(Request, duration);
        }

        protected virtual InvocationResult BindContext(InvocationContext context)
        {
            // Bind invocation information
            Context = context;
            try
            {
                BindProperties(Invocation.Payload);
            }
            catch (Exception ex)
            {
                InvocationEventSource.Log.BindingError(ex);
                return InvocationResult.Faulted(ex);
            }
            return null;
        }

        protected virtual void BindProperties(Dictionary<string, string> dictionary)
        {
            // PERF: Possible optimization is to build a DynamicMethod using Expressions that takes a dictionary and does the following for each property
            //  IF dictionary contains property name THEN convert and set value without using reflection
            foreach (var prop in GetBindableProperties().Cast<PropertyDescriptor>())
            {
                string value;
                if (dictionary.TryGetValue(prop.Name, out value))
                {
                    BindProperty(prop, value);
                }
            }
        }

        protected virtual PropertyDescriptorCollection GetBindableProperties()
        {
            return TypeDescriptor.GetProperties(this);
        }

        protected virtual void BindProperty(PropertyDescriptor prop, string value)
        {
            object convertedValue = ConvertPropertyValue(prop, value);
            prop.SetValue(this, convertedValue);
        }

        private IList<TypeConverter> _converters = new List<TypeConverter>() {
            new SqlConnectionStringBuilderConverter()
        };

        protected virtual object ConvertPropertyValue(PropertyDescriptor prop, string value)
        {
            var converter = _converters.FirstOrDefault(c => c.CanConvertFrom(typeof(string)) && c.CanConvertTo(prop.PropertyType));
            if (converter != null)
            {
                return converter.ConvertFromString(value);
            }
            return prop.Converter.ConvertFromString(value);
        }

        private class SqlConnectionStringBuilderConverter : TypeConverter
        {
            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            {
                return sourceType == typeof(string);
            }

            public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
            {
                return destinationType == typeof(SqlConnectionStringBuilder);
            }

            public override object ConvertFrom(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)
            {
                string strVal = value as string;
                if (strVal == null)
                {
                    return null;
                }
                return new SqlConnectionStringBuilder(strVal);
            }
        }
    }
}