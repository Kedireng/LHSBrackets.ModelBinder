using System;

using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace LHSBrackets.ModelBinder
{
    public class FilterModelBinderProvider : IModelBinderProvider
    {
        public IModelBinder? GetBinder(ModelBinderProviderContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (context.Metadata.UnderlyingOrModelType.IsAssignableToGenericType(typeof(IFilterRequest<>)))
            {
                return new FilterModelBinder();
            }

            return null;
        }
    }
}