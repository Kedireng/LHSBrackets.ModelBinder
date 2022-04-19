using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace LHSBrackets.ModelBinder
{
    public class FilterModelBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext == null)
                throw new ArgumentNullException(nameof(bindingContext));

            var requestModel = Activator.CreateInstance(bindingContext.ModelType);
            if (requestModel is FilterRequest filterRequest == false)
                throw new ArgumentException($"The modeltype {requestModel?.GetType()} does not inherit from {typeof(FilterRequest)}");

            var properties = bindingContext.ModelType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                if (!typeof(FilterOperations).IsAssignableFrom(prop.PropertyType))
                {
                    var converter = TypeDescriptor.GetConverter(prop.PropertyType);
                    object convertedValue;
                    try
                    {
                        var value = bindingContext.ValueProvider.GetValue($"{prop.Name}".ToLower());
                        if (value.FirstValue == null) continue;
                        convertedValue = converter.ConvertFromString(null, new CultureInfo("en-GB"), (string)value.Values);
                        prop.SetValue(requestModel, convertedValue);
                        continue;
                    }
                    catch (NotSupportedException)
                    {
                        throw;
                    }
                }

                var inst = (FilterOperations)Activator.CreateInstance(prop.PropertyType)!;
                if (inst.InnerType.IsClass && !inst.InnerType.Namespace!.StartsWith("System"))
                {
                    var innerProperties = inst.InnerType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                    foreach (var innerProp in innerProperties)
                    {
                        foreach (var operation in Enum.GetValues<FilterOperationEnum>())
                        {
                            var valueProviderResult = bindingContext.ValueProvider
                                .GetValue($"{prop.Name}[{innerProp.Name}][{operation}]".ToLower()); // ex: "author[email][eq]"
                            if (valueProviderResult.Length > 0)
                            {
                                var param = Expression.Parameter(inst.InnerType);
                                var body = Expression.Property(param, innerProp);
                                Type myGeneric = typeof(Func<,>);
                                Type constructedClass = myGeneric.MakeGenericType(inst.InnerType, innerProp.PropertyType);
                                var exp = Expression.Lambda(constructedClass, body, param);

                                SetValueOnProperty(requestModel, prop, operation, (string)valueProviderResult.Values, exp);
                            }
                        }
                    }
                }

                EnsurePrimitiveType(prop);

                foreach (var operation in Enum.GetValues<FilterOperationEnum>())
                {
                    var valueProviderResult = bindingContext.ValueProvider.GetValue($"{prop.Name}[{operation.ToString()}]".ToLower()); // ex: "author[eq]"
                    if (valueProviderResult.Length > 0)
                        SetValueOnProperty(requestModel, prop, operation, (string)valueProviderResult.Values, null);
                }

                // support regular query param without operation ex: categoryId=2
                var valueProviderResult2 = bindingContext.ValueProvider.GetValue($"{prop.Name}".ToLower()); // ex: "author[eq]"
                if (valueProviderResult2.Length > 0)
                    SetValueOnProperty(requestModel, prop, FilterOperationEnum.Eq, (string)valueProviderResult2.Values, null);
            }

            bindingContext.Result = ModelBindingResult.Success(requestModel);
            return Task.CompletedTask;
        }

        private static void EnsurePrimitiveType(PropertyInfo prop)
        {
            // TODO: validate for primitive types, string, datetime and stuff
            return;
        }

        private static void SetValueOnProperty(object? requestModel, PropertyInfo prop, FilterOperationEnum operation, string value, LambdaExpression? selector)
        {
            var propertyObject = prop.GetValue(requestModel, null);
            if(propertyObject == null) // property not instantiated
            {
                propertyObject = Activator.CreateInstance(prop.PropertyType);
                prop.SetValue(requestModel, propertyObject);
            }

            var method = prop.PropertyType.GetMethod(nameof(FilterOperations.SetValue), BindingFlags.Instance | BindingFlags.NonPublic);
            method!.Invoke(propertyObject, new object[] { operation, value, selector });
        }
    }
}