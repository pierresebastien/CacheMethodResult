using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CacheMethodResult.Fody.Extensions;
using CacheMethodResult.Fody.Helpers;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace CacheMethodResult.Fody
{
	public class MethodCache
	{
		private readonly ReferenceFinder _referenceFinder;

		public MethodCache(ModuleDefinition moduleDefinition)
		{
			_referenceFinder = new ReferenceFinder(moduleDefinition);
		}

		public void Cache(MethodDefinition method, CustomAttribute attribute, TypeDefinition typeDefinition)
		{
			if (method.ReturnType.FullName == method.Module.ImportType(typeof (void)).FullName)
			{
				return;
			}
			if (method.IsConstructor)
			{
				return;
			}

			method.Body.InitLocals = true;
			method.Body.SimplifyMacros();

			if (method.Body.Instructions.All(x => x.OpCode != OpCodes.Ret))
			{
				return;
			}

			// ref to retrieve method and custom attribute
			var getMethodFromHandleRef =
				_referenceFinder.GetMethodReference(typeof (MethodBase),
					md => md.Name == "GetMethodFromHandle" && md.Parameters.Count == 2);
			var getCustomAttributesRef =
				_referenceFinder.GetMethodReference(typeof (MemberInfo),
					md => md.Name == "GetCustomAttributes" && md.Parameters.Count == 2);
			var getTypeFromHandleRef = _referenceFinder.GetMethodReference(typeof (Type), md => md.Name == "GetTypeFromHandle");

			// types ref
			var methodBaseTypeRef = _referenceFinder.GetTypeReference(typeof (MethodBase));
			var parameterTypeRef = _referenceFinder.GetTypeReference(typeof (object));
			var parametersArrayTypeRef = _referenceFinder.GetTypeReference(typeof (object[]));

			// variable definitions
			var methodVariableDefinition = method.AddVariable(methodBaseTypeRef, "__fody$method");
			var attributeVariableDefinition = method.AddVariable(attribute.AttributeType, "__fody$attribute");
			var parametersVariableDefinition = method.AddVariable(parametersArrayTypeRef, "__fody$parameters");
			var cachevalVariableDefinition = method.AddVariable(method.ReturnType, "__fody$cacheval");
			var retvalVariableDefinition = method.AddVariable(method.ReturnType, "__fody$retval");

			ILProcessor processor = method.Body.GetILProcessor();
			Instruction methodBodyFirstInstruction = method.Body.Instructions.First();

			var retrieveMethodRef =
				_referenceFinder.GetMethodReference(attribute.AttributeType, md => md.Name == "Retrieve")
					.MakeGeneric(method.ReturnType);
			var storeMethodRef =
				_referenceFinder.GetMethodReference(attribute.AttributeType, md => md.Name == "Store")
					.MakeGeneric(method.ReturnType);

			// create instructions
			var getAttributeInstanceInstructions =
				GetAttributeInstanceInstructions(processor, method, attribute, attributeVariableDefinition, methodVariableDefinition,
					getCustomAttributesRef, getTypeFromHandleRef, getMethodFromHandleRef);
			var createParametersArrayInstructions =
				CreateParametersArrayInstructions(processor, method, parameterTypeRef, parametersVariableDefinition);
			var retrieveInstructions =
				GetCallRetrieveInstructions(processor, attributeVariableDefinition, methodVariableDefinition,
					parametersVariableDefinition, cachevalVariableDefinition, retrieveMethodRef);
			var saveRetvalInstructions = GetSaveRetvalInstructions(processor, retvalVariableDefinition);
			var storeInstructions =
				GetCallStoreInstructions(processor, attributeVariableDefinition, retvalVariableDefinition, methodVariableDefinition,
					parametersVariableDefinition, storeMethodRef);
			var methodBodyReturnInstructions = GetMethodBodyReturnInstructions(processor, retvalVariableDefinition);
			var methodBodyReturnInstruction = methodBodyReturnInstructions.First();

			var tmp = GetReturnIfFoundInstructions(processor, cachevalVariableDefinition, methodBodyFirstInstruction);

			ReplaceRetInstructions(processor, saveRetvalInstructions.Concat(storeInstructions).First());

			// insert instructions
			processor.InsertBefore(methodBodyFirstInstruction, getAttributeInstanceInstructions);
			processor.InsertBefore(methodBodyFirstInstruction, createParametersArrayInstructions);
			processor.InsertBefore(methodBodyFirstInstruction, retrieveInstructions);
			processor.InsertAfter(method.Body.Instructions.Last(), methodBodyReturnInstructions);
			processor.InsertBefore(methodBodyReturnInstruction, saveRetvalInstructions);
			processor.InsertBefore(methodBodyReturnInstruction, storeInstructions);
			processor.InsertBefore(methodBodyFirstInstruction, tmp);

			method.Body.OptimizeMacros();
		}

		private static IEnumerable<Instruction> GetCallRetrieveInstructions(ILProcessor processor,
			VariableDefinition attributeVariableDefinition,
			VariableDefinition methodVariableDefinition,
			VariableDefinition parametersVariableDefinition,
			VariableDefinition cacheObjectVariableDefinition,
			MethodReference retrieveMethodRef)
		{
			// Call __fody$attribute.OnEntry("{methodName}")
			return new List<Instruction>
			       {
				       processor.Create(OpCodes.Ldloc_S, attributeVariableDefinition),
				       processor.Create(OpCodes.Ldloc_S, methodVariableDefinition),
				       processor.Create(OpCodes.Ldloc_S, parametersVariableDefinition),
				       processor.Create(OpCodes.Callvirt, retrieveMethodRef),
				       processor.Create(OpCodes.Stloc_S, cacheObjectVariableDefinition)
			       };
		}

		private static IList<Instruction> GetCallStoreInstructions(ILProcessor processor,
			VariableDefinition attributeVariableDefinition,
			VariableDefinition retvalVariableDefinition,
			VariableDefinition methodVariableDefinition,
			VariableDefinition parametersVariableDefinition,
			MethodReference storeMethodRef)
		{
			// Call __fody$attribute.OnExit("{methodName}")
			return new List<Instruction>
			       {
				       processor.Create(OpCodes.Ldloc_S, attributeVariableDefinition),
				       processor.Create(OpCodes.Ldloc_S, retvalVariableDefinition),
				       processor.Create(OpCodes.Ldloc_S, methodVariableDefinition),
				       processor.Create(OpCodes.Ldloc_S, parametersVariableDefinition),
				       processor.Create(OpCodes.Callvirt, storeMethodRef)
			       };
		}

		private static IEnumerable<Instruction> GetReturnIfFoundInstructions(ILProcessor processor,
			VariableDefinition cacheObjectVariableDefinition,
			Instruction methodBodyFirstInstruction)
		{
			return new List<Instruction>
			       {
				       processor.Create(OpCodes.Ldloc_S, cacheObjectVariableDefinition),
				       processor.Create(OpCodes.Ldnull),
				       processor.Create(OpCodes.Ceq),
				       processor.Create(OpCodes.Brtrue_S, methodBodyFirstInstruction),
				       processor.Create(OpCodes.Ldloc_S, cacheObjectVariableDefinition),
				       processor.Create(OpCodes.Ret)
			       };
		}

		private static IEnumerable<Instruction> GetAttributeInstanceInstructions(ILProcessor processor,
			MethodReference method,
			ICustomAttribute attribute,
			VariableDefinition
				attributeVariableDefinition,
			VariableDefinition methodVariableDefinition,
			MethodReference getCustomAttributesRef,
			MethodReference getTypeFromHandleRef,
			MethodReference getMethodFromHandleRef)
		{
			// Get the attribute instance (this gets a new instance for each invocation.
			// Might be better to create a static class that keeps a track of a single
			// instance per method and we just refer to that)
			return new List<Instruction>
			       {
				       processor.Create(OpCodes.Ldtoken, method),
				       processor.Create(OpCodes.Ldtoken, method.DeclaringType),
				       processor.Create(OpCodes.Call, getMethodFromHandleRef),
				       // Push method onto the stack, GetMethodFromHandle, result on stack
				       processor.Create(OpCodes.Stloc_S, methodVariableDefinition), // Store method in __fody$method
				       processor.Create(OpCodes.Ldloc_S, methodVariableDefinition),
				       processor.Create(OpCodes.Ldtoken, attribute.AttributeType),
				       processor.Create(OpCodes.Call, getTypeFromHandleRef),
				       // Push method + attribute onto the stack, GetTypeFromHandle, result on stack
				       processor.Create(OpCodes.Ldc_I4_0),
				       processor.Create(OpCodes.Callvirt, getCustomAttributesRef),
				       // Push false onto the stack (result still on stack), GetCustomAttributes
				       processor.Create(OpCodes.Ldc_I4_0),
				       processor.Create(OpCodes.Ldelem_Ref), // Get 0th index from result
				       processor.Create(OpCodes.Castclass, attribute.AttributeType),
				       processor.Create(OpCodes.Stloc_S, attributeVariableDefinition) // Cast to attribute stor in __fody$attribute
			       };
		}

		private static IEnumerable<Instruction> CreateParametersArrayInstructions(ILProcessor processor,
			MethodDefinition method,
			TypeReference objectTypeReference,
			VariableDefinition arrayVariable
			/*parameters*/)
		{
			var createArray = new List<Instruction>
			                  {
				                  processor.Create(OpCodes.Ldc_I4, method.Parameters.Count), //method.Parameters.Count
				                  processor.Create(OpCodes.Newarr, objectTypeReference), // new object[method.Parameters.Count]
				                  processor.Create(OpCodes.Stloc, arrayVariable)
				                  // var objArray = new object[method.Parameters.Count]
			                  };

			foreach (var p in method.Parameters)
			{
				createArray.AddRange(ILHelper.ProcessParam(p, arrayVariable));
			}

			return createArray;
		}

		private static IList<Instruction> GetSaveRetvalInstructions(ILProcessor processor,
			VariableDefinition retvalVariableDefinition)
		{
			return retvalVariableDefinition == null || processor.Body.Instructions.All(i => i.OpCode != OpCodes.Ret)
				? new Instruction[0]
				: new[] {processor.Create(OpCodes.Stloc_S, retvalVariableDefinition)};
		}

		private static IList<Instruction> GetMethodBodyReturnInstructions(ILProcessor processor,
			VariableDefinition retvalVariableDefinition)
		{
			var instructions = new List<Instruction>();
			if (retvalVariableDefinition != null)
			{
				instructions.Add(processor.Create(OpCodes.Ldloc_S, retvalVariableDefinition));
			}
			instructions.Add(processor.Create(OpCodes.Ret));
			return instructions;
		}

		private static void ReplaceRetInstructions(ILProcessor processor, Instruction methodEpilogueFirstInstruction)
		{
			// We cannot call ret inside a try/catch block. Replace all ret instructions with
			// an unconditional branch to the start of the OnExit epilogue
			var retInstructions = (from i in processor.Body.Instructions
				where i.OpCode == OpCodes.Ret
				select i).ToList();

			foreach (var instruction in retInstructions)
			{
				instruction.OpCode = OpCodes.Br_S;
				instruction.Operand = methodEpilogueFirstInstruction;
			}
		}
	}
}
