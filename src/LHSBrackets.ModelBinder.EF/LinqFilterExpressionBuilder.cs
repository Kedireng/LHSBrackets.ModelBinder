
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace LHSBrackets.ModelBinder.EF
{
    public static class LinqFilterExpressionBuilder
    {
        public static Expression<Func<TEntity, bool>>? GetAllFilters<TEntity, TKey>(
            this TKey query)
            where TKey : class, IFilterRequest<TEntity>
        {
            return GetAllFiltersLocal<TEntity, TKey>(query, typeof(TEntity));
        }

        private static Expression<Func<TEntity, bool>>? GetAllFiltersLocal<TEntity, TKey>(
            this TKey query, Type type, MemberExpression? selector = null, ParameterExpression? parameter = null)
        {
            Type queryType = typeof(TKey);

            PropertyInfo[] entityProps = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            List<Expression<Func<TEntity, bool>>> filterExpressions = new();

            foreach (PropertyInfo? prop in queryType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(x => typeof(FilterOperations).IsAssignableFrom(x.PropertyType) || x.PropertyType.IsAssignableToGenericType(typeof(IFilterRequest<>))))
            {
                Type? propType = null;
                if (prop.PropertyType.IsAssignableToGenericType(typeof(IFilterRequest<>)))
                {
                    Type[] interfaces = prop.PropertyType.GetInterfaces();
                    foreach (Type t in interfaces)
                    {
                        if (t.GetGenericTypeDefinition() == typeof(IFilterRequest<>))
                        {
                            propType = t.GetGenericArguments()[0];
                        }
                    }
                }
                else if (prop.PropertyType.GetGenericArguments()[0].IsAssignableToGenericType(typeof(IFilterRequest<>)))
                {
                    Type? baseType = prop.PropertyType.GetGenericArguments()[0];
                    Type[] interfaces = baseType.GetInterfaces();
                    foreach (Type t in interfaces)
                    {
                        if (t.GetGenericTypeDefinition() == typeof(IFilterRequest<>))
                        {
                            propType = t.GetGenericArguments()[0];
                        }
                    }
                }
                else
                {
                    propType = prop.PropertyType.GetGenericArguments()[0];
                }
                PropertyInfo? found = entityProps.FirstOrDefault(x => x.Name == prop.Name);

                if (found == null)
                {
                    continue;
                }

                if (found.PropertyType != propType && Nullable.GetUnderlyingType(found.PropertyType) != propType)
                {
                    throw new Exception($"Query and model property types dont match for {found.Name} property!");
                }


                if (prop.PropertyType.IsAssignableToGenericType(typeof(IFilterRequest<>)))
                {
                    LambdaExpression exp;
                    MemberExpression body;
                    ParameterExpression param;
                    if (selector != null && parameter != null)
                    {
                        param = parameter;
                        body = Expression.PropertyOrField(selector, found.Name);
                        Type myGeneric = typeof(Func<,>);
                        Type constructedClass = myGeneric.MakeGenericType(parameter.Type, found.PropertyType);
                        exp = Expression.Lambda(constructedClass, body, parameter);
                    }
                    else
                    {
                        param = Expression.Parameter(typeof(TEntity));
                        body = Expression.PropertyOrField(param, found.Name);
                        Type myGeneric = typeof(Func<,>);
                        Type constructedClass = myGeneric.MakeGenericType(typeof(TEntity), found.PropertyType);
                        exp = Expression.Lambda(constructedClass, body, param);
                    }

                    (Expression left, ParameterExpression param1) = CreateLeftExpression(typeof(TEntity), exp);
                    Expression lambdaExpression = Expression.Lambda(left, param1);

                    object? val = prop.GetValue(query, null);

                    if (val == null || propType == null)
                    {
                        continue;
                    }
                    MethodInfo method = typeof(LinqFilterExpressionBuilder).GetMethod(nameof(GetAllFiltersLocal),
                                BindingFlags.NonPublic | BindingFlags.Static)!;
                    method = method.MakeGenericMethod(typeof(TEntity), prop.PropertyType);
                    Expression<Func<TEntity, bool>>? ex = (Expression<Func<TEntity, bool>>?)method.Invoke(null, new object[] { val, propType, body, param })!;

                    if (ex != null)
                    {
                        filterExpressions.Add(ex);
                    }
                }
                else
                {
                    LambdaExpression exp;
                    if (selector != null && parameter != null)
                    {
                        MemberExpression body = Expression.PropertyOrField(selector, found.Name);
                        Type myGeneric = typeof(Func<,>);
                        Type constructedClass = myGeneric.MakeGenericType(parameter.Type, found.PropertyType);
                        exp = Expression.Lambda(constructedClass, body, parameter);
                    }
                    else
                    {
                        ParameterExpression param = Expression.Parameter(typeof(TEntity));
                        MemberExpression body = Expression.PropertyOrField(param, found.Name);
                        Type myGeneric = typeof(Func<,>);
                        Type constructedClass = myGeneric.MakeGenericType(typeof(TEntity), found.PropertyType);
                        exp = Expression.Lambda(constructedClass, body, param);
                    }

                    FilterOperations fo = (FilterOperations)prop.GetValue(query, null)!;

                    filterExpressions.AddRange(CreateFilters<TEntity>(exp, fo));
                }
            }

            if (!filterExpressions.Any())
            {
                return null;
            }

            Expression<Func<TEntity, bool>> res = filterExpressions.First();

            if (filterExpressions.Count == 1)
            {
                return res;
            }

            foreach (Expression<Func<TEntity, bool>> exp in filterExpressions.GetRange(1, filterExpressions.Count - 1))
            {
                res = res.And(exp);
            }

            return res;
        }

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
            TKey query) where TKey : class, IFilterRequest<TEntity>
        {
            Expression<Func<TEntity, bool>>? filters = GetAllFilters<TEntity, TKey>(query);

            if (filters == null)
            {
                return queryable;
            }

            queryable = queryable.Where(filters);

            return queryable;
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
            {
                return source;
            }

            List<Expression<Func<TEntity, bool>>> filterExpressions = new();


            filterExpressions.AddRange(CreateFilters<TEntity>(selector, filters));
            foreach (Expression<Func<TEntity, bool>> expression in filterExpressions)
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

            MethodInfo[] methods = typeof(string).GetMethods();
            return (methods.Single(m => m.Name == methodName
            && m.GetParameters().Length == 1
            && m.GetParameters().First().ParameterType == typeof(String)), inverted);
        }

        private static List<Expression<Func<TEntity, bool>>> CreateFilters<TEntity>(LambdaExpression selector, FilterOperations filterOperations)
        {
            List<Expression<Func<TEntity, bool>>> expressions = new();

            if (filterOperations == null)
            {
                return expressions;
            }

            foreach ((FilterOperationEnum operation, IEnumerable<object> values, bool hasMultipleValues, LambdaExpression? selector) filterOperation in filterOperations)
            {
                IEnumerator<object> enumerator = filterOperation.values.GetEnumerator();
                bool any = enumerator.MoveNext();
                if (!any)
                {
                    continue;
                }

                LambdaExpression localSelector = selector;
                Expression<Func<TEntity, bool>>? exp = null;

                if (filterOperation.selector != null)
                {
                    MemberExpression? selBody = selector.Body as MemberExpression;
                    MemberExpression body = Expression.Property(selBody, (PropertyInfo)((MemberExpression)filterOperation.selector.Body).Member);
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
                    (MethodInfo? method, bool inverted) = MapOperationToStringMethod(filterOperation.operation);
                    exp = CreateStringExpression<TEntity>(method, localSelector, enumerator.Current, inverted);
                }
                else if (!filterOperation.hasMultipleValues)
                {
                    {
                        Func<Expression, Expression, Expression> linqOperationExp = MapOperationToLinqExpression(filterOperation.operation);
                        exp = CreateBasicExpression<TEntity>(linqOperationExp, localSelector, enumerator.Current);
                    }
                }
                else
                {
                    exp = CreateContainsExpression<TEntity>(localSelector, filterOperation.values, filterOperation.operation == FilterOperationEnum.Nin);
                }

                expressions.Add(exp);
            }

            return expressions;
        }

        private static Expression<Func<TEntity, bool>> CreateStringExpression<TEntity>(
            MethodInfo stringMethod,
            LambdaExpression selector,
            object value,
            bool invert = false)
        {
            (Expression? left, ParameterExpression? param) = CreateLeftExpression(typeof(TEntity), selector);
            left = Expression.Convert(left, selector.ReturnType);
            Expression right = Expression.Constant(value);
            MethodInfo[] methods = typeof(string).GetMethods();
            MethodInfo toLowerMethod = methods.Single(m => m.Name == nameof(String.ToLower)
            && m.GetParameters().Length == 0);

            Expression toStringLeft;
            if (selector.ReturnType != typeof(string))
            {
                MethodInfo toStringMethodLeft = selector.ReturnType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Single(p => p.Name == "ToString" && p.GetParameters().Length == 0);
                toStringLeft = Expression.Call(left, toStringMethodLeft);
            }
            else
            {
                toStringLeft = left;
            }

            Expression toStringRight;
            if (value.GetType() != typeof(string))
            {
                MethodInfo toStringMethodRight = value.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Single(p => p.Name == "ToString" && p.GetParameters().Length == 0);
                toStringRight = Expression.Call(right, toStringMethodRight);
            }
            else
            {
                toStringRight = right;
            }

            Expression loweredLeft = Expression.Call(toStringLeft, toLowerMethod);
            Expression loweredRight = Expression.Call(toStringRight, toLowerMethod);
            Expression containsExpression = Expression.Call(loweredLeft, stringMethod, loweredRight);
            if (invert == true)
            {
                containsExpression = Expression.Not(containsExpression);
            }

            Expression<Func<TEntity, bool>> lambdaExpression = Expression.Lambda<Func<TEntity, bool>>(containsExpression, param);
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
            (Expression? left, ParameterExpression? param) = CreateLeftExpression(typeof(TEntity), selector);
            Type listType = typeof(List<>).MakeGenericType(new[] { selector.ReturnType });
            dynamic list = Activator.CreateInstance(listType)!;
            foreach (dynamic val in values)
            {
                Type t = Nullable.GetUnderlyingType(selector.ReturnType) ?? selector.ReturnType;

                list.Add(Convert.ChangeType(val, t));
            }
            Expression right = Expression.Constant(list);

            MethodInfo containsMethodRef = typeof(Enumerable).GetMethods(BindingFlags.Static | BindingFlags.Public)
                   .Single(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2);
            MethodInfo containsMethod = containsMethodRef.MakeGenericMethod(selector.ReturnType);

            Expression containsExpression = Expression.Call(containsMethod, right, left);
            if (invert == true)
            {
                containsExpression = Expression.Not(containsExpression);
            }

            Expression<Func<TEntity, bool>> lambdaExpression = Expression.Lambda<Func<TEntity, bool>>(containsExpression, param);
            return lambdaExpression;
        }

        private static Expression<Func<TEntity, bool>> CreateBasicExpression<TEntity>(
            Func<Expression, Expression, Expression> expressionOperator,
            LambdaExpression selector,
            object value)
        {
            (Expression? left, ParameterExpression? param) = CreateLeftExpression(typeof(TEntity), selector);
            Type nullableType = MakeNullableType(selector.ReturnType);
            left = Expression.Convert(left, nullableType);
            Expression right = Expression.Constant(value, nullableType);

            Expression finalExpression = expressionOperator.Invoke(left, right);
            Expression<Func<TEntity, bool>> lambdaExpression = Expression.Lambda<Func<TEntity, bool>>(finalExpression, param);
            return lambdaExpression;
        }

        /// <summary>
        /// This will take care of nested navigation properties as well
        /// </summary>
        static (Expression Left, ParameterExpression Param) CreateLeftExpression(Type parameterType, LambdaExpression selector)
        {
            string parameterName = "x";
            ParameterExpression parameter = Expression.Parameter(parameterType, parameterName);

            string propertyName = GetParameterName(selector);
            Expression memberExpression = parameter;
            foreach (string? member in propertyName.Split('.').Skip(1))
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
            {
                return type;
            }

            if (type == typeof(string))
            {
                return type;
            }

            return typeof(Nullable<>).MakeGenericType(type);
        }
    }
}
