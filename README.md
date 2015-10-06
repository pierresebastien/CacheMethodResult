## This is an add-in for [Fody](https://github.com/Fody/Fody/) 

Caches return values of methods decorated with an attribute.

[Introduction to Fody](http://github.com/Fody/Fody/wiki/SampleUsage)

## Nuget

Nuget package http://nuget.org/packages/CacheAttribute.Fody 

To Install from the Nuget Package Manager Console 
    
    PM> Install-Package CacheAttribute.Fody

### Base Cache Code	
	
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
	public abstract class BaseCacheAttribute : Attribute
	{
		public abstract T Retrieve<T>(MethodBase method, object[] args);

		public abstract void Store<T>(T t, MethodBase method, object[] args);
	}
	
### Your Code

Define your cache implementation by deriving from ````BaseCacheAttribute````:

	public class CacheAttribute : BaseCacheAttribute
	{
		public CacheAttribute()
		{
			ArgumentOrder = -1;
			CacheValidityInMinutes = -1;
		}

		public string ReturnType { get; set; }

		public int ArgumentOrder { get; set; }

		public int CacheValidityInMinutes { get; set; }
	
	    public override T Retrieve<T>(MethodBase method, object[] args)
	    {
			if(string.IsNullOrWhiteSpace(ReturnType))
			{
				var m = method as MethodInfo;
				ReturnType = m.ReturnType.Name;
			}
			return Context.Current.Cache.Get<T>(GenerateCacheKey(ReturnType, ArgumentOrder, args));
	    }
	
	    public override void Store<T>(T t, MethodBase method, object[] args)
	    {
	        string key = GenerateCacheKey(CacheKey, ArgumentOrder, args);
			if(CacheValidityInMinutes > 0)
			{
				Context.Current.Cache.Set(key, t, TimeSpan.FromMinutes(CacheValidityInMinutes));
			}
			else
			{
				Context.Current.Cache.Set(key, t);
			}
	    }
		
		private string GenerateCacheKey(string objectName, int argumentOrder, object[] args)
		{
			string key = objectName;
			if (argumentOrder >= 0)
			{
				key += ":" + args[argumentOrder];
			}
			return key;
		}
	}
	
	public class Sample
	{
		[Cache(ArgumentOrder = 0)]
		public string GetStringById(string id)
		{
			string value;
		    switch(id){
				case "test":
					value = "foo";
				default:
					value = "bar";
			}
			return value;
		}
	}

### What gets compiled
	
	public class Sample
	{
		public string GetStringById(string id)
		{
		    MethodBase method = methodof(Sample.Method, Sample);
		    CacheAttribute attribute = (CacheAttribute) method.GetCustomAttributes(typeof(CacheAttribute), false)[0];
		    
			object[] args = new object[1] { (object) id };
			
			var returnValue = attribute.Retrieve<string>(method, args);
			if(returnValue != null)
			{
				return returnValue
			}
			
			string value;
		    switch(id){
				case "test":
					value = "foo";
				default:
					value = "bar";
			}
			
			attribute.Store(value, method, args);
			
			return value;
		}
	}

## Known limitations

- No log when compiling
- Add unit tests