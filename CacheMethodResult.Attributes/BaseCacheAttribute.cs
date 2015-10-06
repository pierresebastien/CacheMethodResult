using System;
using System.Reflection;

namespace CacheMethodResult.Attributes
{
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
	public abstract class BaseCacheAttribute : Attribute
	{
		public abstract T Retrieve<T>(MethodBase method, object[] args);

		public abstract void Store<T>(T t, MethodBase method, object[] args);
	}
}
