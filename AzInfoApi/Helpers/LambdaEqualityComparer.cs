using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AzInfoApi.Helpers
{
    // Helper class for construction
    public static class LambdaEqualityComparer
    {
        public static LambdaEqualityComparer<TSource, TKey>
            Create<TSource, TKey>(Func<TSource, TKey> projection)
        {
            return new LambdaEqualityComparer<TSource, TKey>(projection);
        }

        public static LambdaEqualityComparer<TSource, TKey>
            Create<TSource, TKey>(TSource ignored,
                                   Func<TSource, TKey> projection)
        {
            return new LambdaEqualityComparer<TSource, TKey>(projection);
        }
    }

    public static class ProjectionEqualityComparer<TSource>
    {
        public static LambdaEqualityComparer<TSource, TKey>
            Create<TKey>(Func<TSource, TKey> projection)
        {
            return new LambdaEqualityComparer<TSource, TKey>(projection);
        }
    }

    public class LambdaEqualityComparer<TSource, TKey>
        : IEqualityComparer<TSource>
    {
        readonly Func<TSource, TKey> projection;
        readonly IEqualityComparer<TKey> comparer;

        public LambdaEqualityComparer(Func<TSource, TKey> projection)
            : this(projection, null)
        {
        }

        public LambdaEqualityComparer(
            Func<TSource, TKey> projection,
            IEqualityComparer<TKey> comparer)
        {
            //projection.ThrowIfNull("projection");
            this.comparer = comparer ?? EqualityComparer<TKey>.Default;
            this.projection = projection;
        }

        public bool Equals(TSource x, TSource y)
        {
            if (x == null && y == null)
            {
                return true;
            }
            if (x == null || y == null)
            {
                return false;
            }
            return comparer.Equals(projection(x), projection(y));
        }

        public int GetHashCode(TSource obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException("obj");
            }
            return comparer.GetHashCode(projection(obj));
        }
    }
}
