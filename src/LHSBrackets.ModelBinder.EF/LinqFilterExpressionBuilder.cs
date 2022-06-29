using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace LHSBrackets.ModelBinder.EF
{
    public static class LinqFilterExpressionBuilder
    {
        /// <summary>
        /// Apply all FilterRequest filters to IQueryable
        /// </summary>
        /// <typeparam name="TEntity"></typeparam>
        /// <typeparam name="TKey"></typeparam>
        /// <param name="queryable"></param>
        /// <param name="query"></param>
        /// <returns></returns>
        public static IQueryable<TEntity> ApplyAllFilters<TEntity, TKey>(
            this IQueryable<TEntity> queryable,
            TKey query) where TKey : FilterRequest<TEntity>
        {
            var type = typeof(TEntity);
            var queryType = query.GetType();

            var entityProps = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            var localQueryable = queryable;

            foreach (var prop in queryType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(x => typeof(FilterOperations).IsAssignableFrom(x.PropertyType)))
            {
                Type? propType = null;
                if (prop.PropertyType.GetGenericArguments()[0].IsAssignableToGenericType(typeof(FilterRequest<>)))
                {
                    Type? baseType = prop.PropertyType.GetGenericArguments()[0];
                    while (null != (baseType = baseType.BaseType))
                    {
                        if (baseType.IsGenericType)
                        {
                            var generic = baseType.GetGenericTypeDefinition();
                            if (generic == typeof(FilterRequest<>))
                            {
                                propType = baseType.GetGenericArguments()[0];
                                break;
                            }
                        }
                    }
                }
                else
                {
                    propType = prop.PropertyType.GetGenericArguments()[0];
                }
                var found = entityProps.FirstOrDefault(x => x.Name == prop.Name && x.PropertyType == propType);

                if (found == null) continue;

                var param = Expression.Parameter(typeof(TEntity));
                var body = Expression.PropertyOrField(param, found.Name);
                Type myGeneric = typeof(Func<,>);
                Type constructedClass = myGeneric.MakeGenericType(typeof(TEntity), propType);
                var exp = Expression.Lambda(constructedClass, body, param);
                var fo = (FilterOperations)prop.GetValue(query, null)!;

                localQueryable = localQueryable.ApplyFilters(exp, fo);
            }

            return localQueryable;
        }

        public static IQueryable<TEntity> ApplyFilters<TEntity, TKey>(
            this IQueryable<TEntity> source,
            Expression<Func<TEntity, TKey>> selector,
            FilterOperations<TKey>? filters)
        {
            return source.ApplyFilters((LambdaExpression)selector, filters);

        }

        public static IQueryable<TEntity> ApplyFilters<TEntity>(
            this IQueryable<TEntity> source,
            LambdaExpression selector,
            FilterOperations? filters)
        {
            if (filters == null)
                return source;

            var filterExpressions = new List<Expression<Func<TEntity, bool>>>();

           
            filterExpressions.AddRange(CreateFilters<TEntity>(selector, filters));
            foreach (var expression in filterExpressions)
            {
                source = source.Where(expression);
            }

            return source;
        }

        private static Func<Expression, Expression, Expression> MapOperationToLinqExpression(FilterOperationEnum filterOperationType)
        {
            switch (filterOperationType)
            {
                case FilterOperationEnum.Eq: return Expression.Equal;
                case FilterOperationEnum.Ne: return Expression.NotEqual;
                case FilterOperationEnum.Gt: return Expression.GreaterThan;
                case FilterOperationEnum.Gte: return Expression.GreaterThanOrEqual;
                case FilterOperationEnum.Lt: return Expression.LessThan;
                case FilterOperationEnum.Lte: return Expression.LessThanOrEqual;
                default: throw new Exception($"Couldn't map enum: {filterOperationType}");
            }
        }

        private static (MethodInfo methodInfo, bool inverted) MapOperationToStringMethod(FilterOperationEnum filterOperationType)
        {
            string? methodName = null;
            bool inverted = false;

            switch (filterOperationType)
            {
                case FilterOperationEnum.Li:
                    methodName = nameof(String.Contains);
                    break;
                case FilterOperationEnum.Nli:
                    methodName = nameof(String.Contains);
                    inverted = true;
                    break;
                case FilterOperationEnum.Sw:
                    methodName = nameof(String.StartsWith);
                    break;
                case FilterOperationEnum.Nsw:
                    methodName = nameof(String.StartsWith);
                    inverted = true;
                    break;
                case FilterOperationEnum.Ew:
                    methodName = nameof(String.EndsWith);
                    break;
                case FilterOperationEnum.New:
                    methodName = nameof(String.EndsWith);
                    inverted = true;
                    break;
                default: throw new Exception($"Couldn't map enum: {filterOperationType}");
            }

            var methods = typeof(string).GetMethods();
            return (methods.Single(m => m.Name == methodName
            && m.GetParameters().Length == 1
            && m.GetParameters().First().ParameterType == typeof(String)), inverted);
        }

        private static List<Expression<Func<TEntity, bool>>> CreateFilters<TEntity>(LambdaExpression selector, FilterOperations filterOperations)
        {
            var expressions = new List<Expression<Func<TEntity, bool>>>();

            foreach (var filterOperation in filterOperations)
            {
                var enumerator = filterOperation.values.GetEnumerator();
                var any = enumerator.MoveNext();
                if (!any) continue;

                var localSelector = selector;

                if (filterOperation.selector != null)
                {
                    var selBody = selector.Body as MemberExpression;
                    var body = Expression.Property(selBody, (PropertyInfo)((MemberExpression)filterOperation.selector.Body).Member);
                    Type myGeneric = typeof(Func<,>);
                    Type constructedClass = myGeneric.MakeGenericType(typeof(TEntity), filterOperation.selector.ReturnType);
                    localSelector = Expression.Lambda(constructedClass, body, selector.Parameters);
                }

                if (filterOperation.operation == FilterOperationEnum.Li
                    || filterOperation.operation == FilterOperationEnum.Nli
                    || filterOperation.operation == FilterOperationEnum.Sw
                    || filterOperation.operation == FilterOperationEnum.Nsw
                    || filterOperation.operation == FilterOperationEnum.Ew
                    || filterOperation.operation == FilterOperationEnum.New)
                {
                    (var method, var inverted) = MapOperationToStringMethod(filterOperation.operation);
                    var exp = CreateStringExpression<TEntity>(method, localSelector, enumerator.Current, inverted);
                    expressions.Add(exp);
                }
                else if (!filterOperation.hasMultipleValues)
                {
                    {
                        var linqOperationExp = MapOperationToLinqExpression(filterOperation.operation);
                        var linqExpression = CreateBasicExpression<TEntity>(linqOperationExp, localSelector, enumerator.Current);
                        expressions.Add(linqExpression);
                    }
                }
                else
                {
                    var linqContainsExpression = CreateContainsExpression<TEntity>(localSelector, filterOperation.values, filterOperation.operation == FilterOperationEnum.Nin);
                    expressions.Add(linqContainsExpression);
                }
            }

            return expressions;
        }

        private static Expression<Func<TEntity, bool>> CreateStringExpression<TEntity>(
            MethodInfo stringMethod,
            LambdaExpression selector,
            object value,
            bool invert = false)
        {
            (var left, var param) = CreateLeftExpression(typeof(TEntity), selector);
            left = Expression.Convert(left, selector.ReturnType);
            Expression right = Expression.Constant(value);
            var methods = typeof(string).GetMethods();
            var toLowerMethod = methods.Single(m => m.Name == nameof(String.ToLower)
            && m.GetParameters().Length == 0);

            Expression loweredLeft = Expression.Call(left, toLowerMethod);
            Expression loweredRight = Expression.Call(right, toLowerMethod);
            Expression containsExpression = Expression.Call(loweredLeft, stringMethod, loweredRight);
            if (invert == true) containsExpression = Expression.Not(containsExpression);

            var lambdaExpression = Expression.Lambda<Func<TEntity, bool>>(containsExpression, param);
            return lambdaExpression;
        }

        /// <summary>
        /// it is nasty AF to work with list of nullables so we just use right hand side's type as it is and convert left to be the same
        /// </summary>
        private static Expression<Func<TEntity, bool>> CreateContainsExpression<TEntity>(
            LambdaExpression selector,
            IEnumerable<object> values,
            bool invert = false)
        {
            (var left, var param) = CreateLeftExpression(typeof(TEntity), selector);
            Type listType = typeof(List<>).MakeGenericType(new[] { selector.ReturnType });
            dynamic list = (dynamic)Activator.CreateInstance(listType)!;
            foreach(dynamic val in values)
            {
                Type t = Nullable.GetUnderlyingType(selector.ReturnType) ?? selector.ReturnType;

                list.Add(Convert.ChangeType(val, t));
            }
            Expression right = Expression.Constant(list);

            var containsMethodRef = typeof(Enumerable).GetMethods(BindingFlags.Static | BindingFlags.Public)
                   .Single(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2);
            MethodInfo containsMethod = containsMethodRef.MakeGenericMethod(selector.ReturnType);

            Expression containsExpression = Expression.Call(containsMethod, right, left);
            if (invert == true) containsExpression = Expression.Not(containsExpression);

            var lambdaExpression = Expression.Lambda<Func<TEntity, bool>>(containsExpression, param);
            return lambdaExpression;
        }

        private static Expression<Func<TEntity, bool>> CreateBasicExpression<TEntity>(
            Func<Expression, Expression, Expression> expressionOperator,
            LambdaExpression selector,
            object value)
        {
            (var left, var param) = CreateLeftExpression(typeof(TEntity), selector);
            var nullableType = MakeNullableType(selector.ReturnType);
            left = Expression.Convert(left, nullableType);
            Expression right = Expression.Constant(value, nullableType);

            var finalExpression = expressionOperator.Invoke(left, right);
            var lambdaExpression = Expression.Lambda<Func<TEntity, bool>>(finalExpression, param);
            return lambdaExpression;
        }

        /// <summary>
        /// This will take care of nested navigation properties as well
        /// </summary>
        static (Expression Left, ParameterExpression Param) CreateLeftExpression(Type parameterType, LambdaExpression selector)
        {
            var parameterName = "x";
            var parameter = Expression.Parameter(parameterType, parameterName);

            var propertyName = GetParameterName(selector);
            var memberExpression = (Expression)parameter;
            foreach (var member in propertyName.Split('.').Skip(1))
            {
                memberExpression = Expression.PropertyOrField(memberExpression, member);
            }
            return (memberExpression, parameter);
        }

        private static string GetParameterName(LambdaExpression expression)
        {
            // this is the case for datetimes, enums and possibly others. Possibly navigation properties.
            if (!(expression.Body is MemberExpression memberExpression))
            {
                memberExpression = (MemberExpression)((UnaryExpression)expression.Body).Operand;
            }

            // // x.Category.Name will become Name
            return memberExpression.ToString();
        }

        private static Type MakeNullableType(Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
                return type;

            if (type == typeof(string))
                return type;

            return typeof(Nullable<>).MakeGenericType(type);
        }
    }
}
