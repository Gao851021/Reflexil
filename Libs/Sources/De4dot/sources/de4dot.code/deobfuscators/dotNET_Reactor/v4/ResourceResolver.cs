﻿/*
    Copyright (C) 2011-2012 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using DeMono.Cecil;
using DeMono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v4 {
	class ResourceResolver {
		ModuleDefinition module;
		EncryptedResource encryptedResource;
		MethodDefinition initMethod;

		public bool Detected {
			get { return encryptedResource.Method != null; }
		}

		public TypeDefinition Type {
			get { return encryptedResource.Type; }
		}

		public MethodDefinition InitMethod {
			get { return initMethod; }
		}

		public bool FoundResource {
			get { return encryptedResource.FoundResource; }
		}

		public ResourceResolver(ModuleDefinition module) {
			this.module = module;
			this.encryptedResource = new EncryptedResource(module);
		}

		public ResourceResolver(ModuleDefinition module, ResourceResolver oldOne) {
			this.module = module;
			this.encryptedResource = new EncryptedResource(module, oldOne.encryptedResource);
		}

		public void find(ISimpleDeobfuscator simpleDeobfuscator) {
			var additionalTypes = new string[] {
				"System.String",
			};
			foreach (var type in module.Types) {
				if (type.BaseType == null || type.BaseType.FullName != "System.Object")
					continue;
				if (!checkFields(type.Fields))
					continue;
				foreach (var method in type.Methods) {
					if (!method.IsStatic || !method.HasBody)
						continue;
					if (!DotNetUtils.isMethod(method, "System.Reflection.Assembly", "(System.Object,System.ResolveEventArgs)") &&
						!DotNetUtils.isMethod(method, "System.Reflection.Assembly", "(System.Object,System.Object)"))
						continue;
					if (!encryptedResource.couldBeResourceDecrypter(method, additionalTypes, false))
						continue;

					encryptedResource.Method = method;
					return;
				}
			}
		}

		bool checkFields(IList<FieldDefinition> fields) {
			if (fields.Count != 3)
				return false;

			var fieldTypes = new FieldTypes(fields);
			if (fieldTypes.count("System.Boolean") != 1)
				return false;
			if (fieldTypes.count("System.Object") == 2)
				return true;
			return fieldTypes.count("System.Reflection.Assembly") == 1 &&
				fieldTypes.count("System.String[]") == 1;
		}

		public void init(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			if (encryptedResource.Method == null)
				return;

			initMethod = findInitMethod(simpleDeobfuscator);
			if (initMethod == null)
				throw new ApplicationException("Could not find resource resolver init method");

			simpleDeobfuscator.deobfuscate(encryptedResource.Method);
			simpleDeobfuscator.decryptStrings(encryptedResource.Method, deob);
			encryptedResource.init(simpleDeobfuscator);
		}

		MethodDefinition findInitMethod(ISimpleDeobfuscator simpleDeobfuscator) {
			var ctor = DotNetUtils.getMethod(Type, ".ctor");
			foreach (var method in Type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.isMethod(method, "System.Void", "()"))
					continue;
				if (method.Body.Variables.Count > 1)
					continue;

				simpleDeobfuscator.deobfuscate(method);
				bool stsfldUsed = false, newobjUsed = false;
				foreach (var instr in method.Body.Instructions) {
					if (instr.OpCode.Code == Code.Stsfld) {
						var field = instr.Operand as FieldReference;
						if (field == null || field.FieldType.FullName != "System.Boolean")
							continue;
						if (!MemberReferenceHelper.compareTypes(Type, field.DeclaringType))
							continue;
						stsfldUsed = true;
					}
					else if (instr.OpCode.Code == Code.Newobj) {
						var calledCtor = instr.Operand as MethodReference;
						if (calledCtor == null)
							continue;
						if (!MemberReferenceHelper.compareMethodReferenceAndDeclaringType(calledCtor, ctor))
							continue;
						newobjUsed = true;
					}
				}
				if (!stsfldUsed || !newobjUsed)
					continue;

				return method;
			}
			return null;
		}

		public EmbeddedResource mergeResources() {
			if (encryptedResource.Resource == null)
				return null;
			DeobUtils.decryptAndAddResources(module, encryptedResource.Resource.Name, () => {
				return QuickLZ.decompress(encryptedResource.decrypt());
			});
			return encryptedResource.Resource;
		}
	}
}
