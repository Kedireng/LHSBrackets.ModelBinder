using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace LHSBrackets.ModelBinder
{
    public class FilterOperations<T> : FilterOperations
    {
        public FilterOperations() : base(typeof(T))
        { }

        public override FilterOperations GetPs() => this;
    }

    public abstract class FilterOperations : List<(FilterOperationEnum operation, IEnumerable<object> values, bool hasMultipleValues)>
    {
        public Type InnerType { get; set; }
        public abstract FilterOperations GetPs();

        public FilterOperations(Type innerType)
        {
            InnerType = innerType;
        }

        internal void SetValue(FilterOperationEnum operation, string value)
        {
            var list = new List<object>();
            bool hasMultipleValues = false;
            switch (operation)
            {
                case FilterOperationEnum.Eq:
                case FilterOperationEnum.Ne:
                case FilterOperationEnum.Gt:
                case FilterOperationEnum.Gte:
                case FilterOperationEnum.Lt:
                case FilterOperationEnum.Lte:
                    var gottenObj = ConvertValue(value);
                    dynamic d = gottenObj;
                    var convertedObject = Convert.ChangeType(d, InnerType);
                    list.Add(convertedObject);
                    hasMultipleValues = false;
                    break;
                case FilterOperationEnum.Li:
                case FilterOperationEnum.Nli:
                case FilterOperationEnum.Sw:
                case FilterOperationEnum.Nsw:
                case FilterOperationEnum.Ew:
                case FilterOperationEnum.New:
                    list.Add(GetString(value));
                    hasMultipleValues = false;
                    break;
                case FilterOperationEnum.In:
                case FilterOperationEnum.Nin:
                    var items = value.Split(",");
                    list.AddRange(items.Select(x => ConvertValue(x.Trim(' '))));
                    hasMultipleValues = true;
                    break;
                default:
                    throw new Exception($"Operation type: {operation} is unhandled.");
            }
            this.Add((operation, list, hasMultipleValues));
        }

        private object ConvertValue(string value)
        {
            var converter = TypeDescriptor.GetConverter(InnerType);
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

        private string GetString(string value)
        {
            if (!typeof(string).IsAssignableFrom(InnerType))
                throw new Exception($"Operation type can only be used with string types.");

            return value;
        }
    }
}