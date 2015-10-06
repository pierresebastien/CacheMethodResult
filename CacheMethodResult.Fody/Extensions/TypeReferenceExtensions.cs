using System;
using Mono.Cecil;

namespace CacheMethodResult.Fody.Extensions
{
	public static class TypeReferenceExtensions
	{
		public static bool DerivesFrom(this TypeReference typeReference, TypeReference expectedBaseTypeReference)
		{
			while (typeReference != null)
			{
				if (typeReference.FullName == expectedBaseTypeReference.FullName)
				{
					return true;
				}
				typeReference = typeReference.Resolve().BaseType;
			}
			return false;
		}

		public static bool DerivesFrom(this TypeDefinition typeDefinition, Type type)
		{
			if (!type.IsClass)
			{
				throw new InvalidOperationException("The <type> argument (" + type.Name + ") must be a class type.");
			}
			var referenceFinder = new ReferenceFinder(typeDefinition.Module);
			var baseTypeDefinition = referenceFinder.GetTypeReference(type);
			return typeDefinition.DerivesFrom(baseTypeDefinition);
		}
	}
}
