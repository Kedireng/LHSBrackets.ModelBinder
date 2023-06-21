using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;

namespace LHSBrackets.ModelBinder
{
    public class FilterOperations<T> : FilterOperations
    {
        public FilterOperations() : base(typeof(T))
        { }

        public override FilterOperations GetPs() => this;
    }

    public abstract class FilterOperations : List<(FilterOperationEnum operation, IEnumerable<object> values, bool hasMultipleValues, LambdaExpression? selector)>
    {
        public Type InnerType { get; set; }
        public abstract FilterOperations GetPs();

        public FilterOperations(Type innerType)
        {
            InnerType = innerType;
        }

        internal void SetValue(FilterOperationEnum operation, string value, LambdaExpression? selector)
        {
            List<object> list = new();
            bool hasMultipleValues = false;
            switch (operation)
            {
                case FilterOperationEnum.Eq:
                case FilterOperationEnum.Ne:
                case FilterOperationEnum.Gt:
                case FilterOperationEnum.Gte:
                case FilterOperationEnum.Lt:
                case FilterOperationEnum.Lte:
                    object gottenObj = ConvertValue(value, selector);
                    dynamic d = gottenObj;
                    object? convertedObject;
                    if (selector == null)
                    {
                        convertedObject = Convert.ChangeType(d, Nullable.GetUnderlyingType(InnerType) ?? InnerType);
                    }
                    else
                    {
                        convertedObject = Convert.ChangeType(d, Nullable.GetUnderlyingType(selector.ReturnType) ?? selector.ReturnType);
                    }

                    list.Add(convertedObject);
                    hasMultipleValues = false;
                    break;
                case FilterOperationEnum.Li:
                case FilterOperationEnum.Nli:
                case FilterOperationEnum.Sw:
                case FilterOperationEnum.Nsw:
                case FilterOperationEnum.Ew:
                case FilterOperationEnum.New:
                    list.Add(value);
                    hasMultipleValues = false;
                    break;
                case FilterOperationEnum.In:
                case FilterOperationEnum.Nin:
                    string[] items = value.Split(",");
                    list.AddRange(items.Select(x => ConvertValue(x.Trim(' '), selector)));
                    hasMultipleValues = true;
                    break;
                default:
                    throw new Exception($"Operation type: {operation} is unhandled.");
            }
            this.Add((operation, list, hasMultipleValues, selector));
        }

        private object ConvertValue(string value, LambdaExpression? selector)
        {
            TypeConverter converter;
            if (selector == null)
            {
                converter = TypeDescriptor.GetConverter(Nullable.GetUnderlyingType(InnerType) ?? InnerType);
            }
            else
            {
                converter = TypeDescriptor.GetConverter(Nullable.GetUnderlyingType(selector.ReturnType) ?? selector.ReturnType);
            }
            object convertedValue;
            try
            {
                convertedValue = converter.ConvertFromString(null, new CultureInfo("en-GB"), value);
            }
            catch (NotSupportedException)
            {
                //RatherEasys is not a valid value for DifficultyEnum
                throw;
                // TODO: do stuff
            }

            return convertedValue;
        }
    }
}