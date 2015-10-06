using System;
using System.Linq;
using Mono.Cecil;

namespace CacheMethodResult.Fody
{
	public class ReferenceFinder
	{
		private readonly ModuleDefinition _moduleDefinition;

		public ReferenceFinder(ModuleDefinition moduleDefinition)
		{
			_moduleDefinition = moduleDefinition;
		}

		public MethodReference GetMethodReference(Type declaringType, Func<MethodDefinition, bool> predicate)
		{
			return GetMethodReference(GetTypeReference(declaringType), predicate);
		}

		public MethodReference GetMethodReference(TypeReference typeReference, Func<MethodDefinition, bool> predicate)
		{
			TypeDefinition typeDefinition = typeReference.Resolve();

			MethodDefinition methodDefinition;
			do
			{
				methodDefinition = typeDefinition.Methods.FirstOrDefault(predicate);
				typeDefinition = typeDefinition.BaseType?.Resolve();
			} while (methodDefinition == null && typeDefinition != null);

			return _moduleDefinition.Import(methodDefinition);
		}

		public TypeReference GetTypeReference(Type type)
		{
			return _moduleDefinition.Import(type);
		}
	}
}
