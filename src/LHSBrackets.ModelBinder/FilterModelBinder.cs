using System;
using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
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
            {
                throw new ArgumentNullException(nameof(bindingContext));
            }

            dynamic? requestModel = BindModel(bindingContext, bindingContext.ModelType);

            bindingContext.Result = ModelBindingResult.Success(requestModel);
            return Task.CompletedTask;
        }

        private dynamic? BindModel(ModelBindingContext bindingContext, Type objectType, string? path = null)
        {
            dynamic? requestModel = Activator.CreateInstance(objectType);
            if (!objectType.IsAssignableToGenericType(typeof(IFilterRequest<>)))
            {
                throw new ArgumentException($"The modeltype {requestModel?.GetType()} does not inherit from {typeof(IFilterRequest<>)}");
            }

            PropertyInfo[] properties = objectType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            System.Collections.Generic.List<string> keys = bindingContext.HttpContext.Request.Query.Keys.ToList();
            foreach (PropertyInfo prop in properties)
            {
                string propPath = path == null ? prop.Name : path + $"[{prop.Name}]";
                if (!typeof(FilterOperations).IsAssignableFrom(prop.PropertyType))
                {
                    if (typeof(IEnumerable).IsAssignableFrom(prop.PropertyType) && prop.PropertyType.GetGenericArguments().Length == 1)
                    {
                        PropertyInfo[] innerProperties = prop.PropertyType.GetGenericArguments()[0].GetProperties(BindingFlags.Public | BindingFlags.Instance);
                        dynamic obj = Activator.CreateInstance(prop.PropertyType)!;
                        int i = 0;
                        while (true)
                        {
                            propPath = propPath + $"[{i}]";

                            if (!keys.Any(x => x.StartsWith(propPath, StringComparison.OrdinalIgnoreCase)))
                            {
                                break;
                            }

                            dynamic? innerObj = BindModel(bindingContext, prop.PropertyType.GetGenericArguments()[0], propPath);

                            if (innerObj == null)
                            {
                                continue;
                            }

                            obj.Add(innerObj);
                            i++;
                        }
                        if (obj.Count != 0)
                        {
                            prop.SetValue(requestModel, obj);
                        }
                        continue;
                    }
                    else if (prop.PropertyType.IsAssignableToGenericType(typeof(IFilterRequest<>)))
                    {
                        prop.SetValue(requestModel, BindModel(bindingContext, prop.PropertyType, propPath));
                        continue;
                    }
                    else
                    {
                        TypeConverter converter = TypeDescriptor.GetConverter(prop.PropertyType);
                        object convertedValue;
                        try
                        {
                            ValueProviderResult value = bindingContext.ValueProvider.GetValue($"{propPath}");
                            if (value.FirstValue == null)
                            {
                                continue;
                            }

                            convertedValue = converter.ConvertFromString(null, new CultureInfo("en-GB"), (string)value.Values);
                            prop.SetValue(requestModel, convertedValue);
                            continue;
                        }
                        catch (NotSupportedException)
                        {
                            throw;
                        }
                    }
                }

                FilterOperations inst = (FilterOperations)Activator.CreateInstance(prop.PropertyType)!;
                if (inst.InnerType.IsClass && !inst.InnerType.Namespace!.StartsWith("System"))
                {
                    PropertyInfo[] innerProperties = inst.InnerType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                    foreach (PropertyInfo innerProp in innerProperties)
                    {
                        foreach (FilterOperationEnum operation in Enum.GetValues<FilterOperationEnum>())
                        {
                            string locPath = propPath + $"[{innerProp.Name}][{operation.ToString().ToLower()}]";
                            string? found = keys.Find(x => x.Equals(locPath, StringComparison.OrdinalIgnoreCase));

                            if (found == null)
                            {
                                continue;
                            }

                            ValueProviderResult valueProviderResult = bindingContext.ValueProvider
                                .GetValue(found); // ex: "author[email][eq]"
                            if (valueProviderResult.Length > 0)
                            {
                                if (inst.InnerType.IsAssignableToGenericType(typeof(IFilterRequest<>)))
                                {
                                    Type? innerType = null;
                                    Type? baseType = inst.InnerType;
                                    while (null != (baseType = baseType.BaseType))
                                    {
                                        if (baseType.IsGenericType)
                                        {
                                            Type generic = baseType.GetGenericTypeDefinition();
                                            if (generic == typeof(FilterRequest<>))
                                            {
                                                innerType = baseType.GetGenericArguments()[0];
                                                break;
                                            }
                                        }
                                    }
                                    PropertyInfo? innerTypeProp = innerType.GetProperty(innerProp.Name);
                                    if (innerTypeProp == null)
                                    {
                                        continue;
                                    }

                                    ParameterExpression param = Expression.Parameter(innerType);
                                    MemberExpression body = Expression.Property(param, innerTypeProp);
                                    Type myGeneric = typeof(Func<,>);
                                    Type constructedClass = myGeneric.MakeGenericType(innerType, innerTypeProp.PropertyType);
                                    LambdaExpression exp = Expression.Lambda(constructedClass, body, param);

                                    SetValueOnProperty(requestModel, prop, operation, (string)valueProviderResult.Values, exp);
                                }
                                else
                                {
                                    ParameterExpression param = Expression.Parameter(inst.InnerType);
                                    MemberExpression body = Expression.Property(param, innerProp);
                                    Type myGeneric = typeof(Func<,>);
                                    Type constructedClass = myGeneric.MakeGenericType(inst.InnerType, innerProp.PropertyType);
                                    LambdaExpression exp = Expression.Lambda(constructedClass, body, param);

                                    SetValueOnProperty(requestModel, prop, operation, (string)valueProviderResult.Values, exp);
                                }
                            }
                        }
                    }
                }
                else
                {

                    EnsurePrimitiveType(prop);

                    foreach (FilterOperationEnum operation in Enum.GetValues<FilterOperationEnum>())
                    {
                        string locPath = propPath + $"[{operation.ToString().ToLower()}]";
                        string? foundInner = keys.Find(x => x.Equals(locPath, StringComparison.OrdinalIgnoreCase));

                        if (foundInner == null)
                        {
                            continue;
                        }
                        ValueProviderResult valueProviderResult = bindingContext.ValueProvider.GetValue(foundInner); // ex: "author[eq]"
                        if (valueProviderResult.Length > 0)
                        {
                            SetValueOnProperty(requestModel, prop, operation, (string)valueProviderResult.Values, null);
                        }
                    }

                    // support regular query param without operation ex: categoryId=2
                    string? found = keys.Find(x => x.Equals(propPath, StringComparison.OrdinalIgnoreCase));

                    if (found == null)
                    {
                        continue;
                    }

                    ValueProviderResult valueProviderResult2 = bindingContext.ValueProvider.GetValue(found); // ex: "author[eq]"
                    if (valueProviderResult2.Length > 0)
                    {
                        SetValueOnProperty(requestModel, prop, FilterOperationEnum.Eq, (string)valueProviderResult2.Values, null);
                    }
                }
            }

            return requestModel;
        }

        private static void EnsurePrimitiveType(PropertyInfo prop)
        {
            // TODO: validate for primitive types, string, datetime and stuff
            return;
        }

        private static void SetValueOnProperty(object? requestModel, PropertyInfo prop, FilterOperationEnum operation, string value, LambdaExpression? selector)
        {
            object? propertyObject = prop.GetValue(requestModel, null);
            if (propertyObject == null) // property not instantiated
            {
                propertyObject = Activator.CreateInstance(prop.PropertyType);
                prop.SetValue(requestModel, propertyObject);
            }

            MethodInfo? method = prop.PropertyType.GetMethod(nameof(FilterOperations.SetValue), BindingFlags.Instance | BindingFlags.NonPublic);
            method!.Invoke(propertyObject, new object[] { operation, value, selector });
        }
    }
}