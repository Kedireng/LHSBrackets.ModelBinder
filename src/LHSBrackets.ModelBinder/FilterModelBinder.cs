using System;
using System.Collections;
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
            {
                throw new ArgumentNullException(nameof(bindingContext));
            }

            object? requestModel = Activator.CreateInstance(bindingContext.ModelType);
            if (!bindingContext.ModelType.IsAssignableToGenericType(typeof(IFilterRequest<>)))
            {
                throw new ArgumentException($"The modeltype {requestModel?.GetType()} does not inherit from {typeof(FilterRequest<>)}");
            }

            PropertyInfo[] properties = bindingContext.ModelType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (PropertyInfo prop in properties)
            {
                if (!typeof(FilterOperations).IsAssignableFrom(prop.PropertyType))
                {
                    if (typeof(IEnumerable).IsAssignableFrom(prop.PropertyType) && prop.PropertyType.GetGenericArguments().Length == 1)
                    {
                        PropertyInfo[] innerProperties = prop.PropertyType.GetGenericArguments()[0].GetProperties(BindingFlags.Public | BindingFlags.Instance);
                        dynamic obj = Activator.CreateInstance(prop.PropertyType)!;
                        int i = 0;
                        while (true)
                        {
                            bool oneFound = false;
                            dynamic innerObj = Activator.CreateInstance(prop.PropertyType.GetGenericArguments()[0])!;
                            foreach (PropertyInfo innerProp in innerProperties)
                            {
                                foreach (FilterOperationEnum operation in Enum.GetValues<FilterOperationEnum>())
                                {
                                    ValueProviderResult valueProviderRes = bindingContext.ValueProvider
                                        .GetValue($"{prop.Name}[{i}][{innerProp.Name}][{operation}]".ToLower()); // ex: "author[email][eq]"
                                    if (valueProviderRes.Length == 0)
                                    {
                                        continue;
                                    }

                                    oneFound = true;
                                    if (prop.PropertyType.GetGenericArguments()[0].IsAssignableToGenericType(typeof(FilterRequest<>)))
                                    {
                                        Type? innerType = null;
                                        Type? baseType = prop.PropertyType.GetGenericArguments()[0];
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
                                        PropertyInfo? innerTypeProp = innerType!.GetProperty(innerProp.Name);
                                        if (innerTypeProp == null)
                                        {
                                            continue;
                                        }

                                        ParameterExpression param = Expression.Parameter(innerType);
                                        MemberExpression body = Expression.Property(param, innerTypeProp);
                                        Type myGeneric = typeof(Func<,>);
                                        Type constructedClass = myGeneric.MakeGenericType(innerType, innerTypeProp.PropertyType);
                                        LambdaExpression exp = Expression.Lambda(constructedClass, body, param);

                                        SetValueOnProperty(innerObj, innerProp, operation, (string)valueProviderRes.Values, null);
                                    }
                                }
                            }
                            if (!oneFound)
                            {
                                break;
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
                    else
                    {
                        TypeConverter converter = TypeDescriptor.GetConverter(prop.PropertyType);
                        object convertedValue;
                        try
                        {
                            ValueProviderResult value = bindingContext.ValueProvider.GetValue($"{prop.Name}".ToLower());
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
                            ValueProviderResult valueProviderResult = bindingContext.ValueProvider
                                .GetValue($"{prop.Name}[{innerProp.Name}][{operation}]".ToLower()); // ex: "author[email][eq]"
                            if (valueProviderResult.Length > 0)
                            {
                                if (inst.InnerType.IsAssignableToGenericType(typeof(FilterRequest<>)))
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
                        ValueProviderResult valueProviderResult = bindingContext.ValueProvider.GetValue($"{prop.Name}[{operation}]".ToLower()); // ex: "author[eq]"
                        if (valueProviderResult.Length > 0)
                        {
                            SetValueOnProperty(requestModel, prop, operation, (string)valueProviderResult.Values, null);
                        }
                    }

                    // support regular query param without operation ex: categoryId=2
                    ValueProviderResult valueProviderResult2 = bindingContext.ValueProvider.GetValue($"{prop.Name}".ToLower()); // ex: "author[eq]"
                    if (valueProviderResult2.Length > 0)
                    {
                        SetValueOnProperty(requestModel, prop, FilterOperationEnum.Eq, (string)valueProviderResult2.Values, null);
                    }
                }
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