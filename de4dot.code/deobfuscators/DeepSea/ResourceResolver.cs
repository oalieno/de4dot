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
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.DeepSea {
	class ResourceResolver : ResolverBase {
		Data30 data30;
		Data40 data40;
		Data41 data41;
		ResourceVersion version = ResourceVersion.Unknown;

		enum ResourceVersion {
			Unknown,
			V3,
			V40,
			V41,
		}

		class Data30 {
			public EmbeddedResource resource;
		}

		class Data40 {
			public FieldDefinition resourceField;
			public MethodDefinition getDataMethod;
			public int magic;
		}

		class Data41 {
			public FieldDefinition resourceField;
			public MethodDefinition resolveHandler2;
			public int magic;
			public bool isTrial;
		}

		class HandlerInfo {
			public MethodDefinition handler;
			public IList<object> args;

			public HandlerInfo(MethodDefinition handler, IList<object> args) {
				this.handler = handler;
				this.args = args;
			}
		}

		public MethodDefinition GetDataMethod {
			get { return data40 != null ? data40.getDataMethod : null; }
		}

		public EmbeddedResource Resource {
			get { return data30 != null ? data30.resource : null; }
		}

		public ResourceResolver(ModuleDefinition module, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob)
			: base(module, simpleDeobfuscator, deob) {
		}

		protected override bool checkResolverInitMethodInternal(MethodDefinition resolverInitMethod) {
			return DotNetUtils.callsMethod(resolverInitMethod, "System.Void System.AppDomain::add_ResourceResolve(System.ResolveEventHandler)");
		}

		protected override bool checkHandlerMethodDesktopInternal(MethodDefinition handler) {
			if (checkHandlerV3(handler)) {
				version = ResourceVersion.V3;
				return true;
			}

			simpleDeobfuscator.deobfuscate(handler);
			if ((data40 = checkHandlerV40(handler)) != null) {
				version = ResourceVersion.V40;
				return true;
			}

			var info = getHandlerArgs41(handler);
			Data41 data41Tmp;
			if (info != null && checkHandlerV41(info, out data41Tmp)) {
				version = ResourceVersion.V41;
				data41 = data41Tmp;
				return true;
			}

			return false;
		}

		HandlerInfo getHandlerArgs41(MethodDefinition handler) {
			var instrs = handler.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				var instr = instrs[i];
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDefinition;
				if (calledMethod == null)
					continue;
				var args = DsUtils.getArgValues(instrs, i);
				if (args == null)
					continue;

				return new HandlerInfo(calledMethod, args);
			}
			return null;
		}

		bool checkHandlerV41(HandlerInfo info, out Data41 data41) {
			data41 = new Data41();
			data41.resolveHandler2 = info.handler;
			data41.resourceField = getLdtokenField(info.handler);
			if (data41.resourceField == null)
				return false;
			int magicArgIndex = getMagicArgIndex41Retail(info.handler);
			if (magicArgIndex < 0) {
				magicArgIndex = getMagicArgIndex41Trial(info.handler);
				data41.isTrial = true;
			}
			var asmVer = module.Assembly.Name.Version;
			if (magicArgIndex < 0 || magicArgIndex >= info.args.Count)
				return false;
			var val = info.args[magicArgIndex];
			if (!(val is int))
				return false;
			if (data41.isTrial)
				data41.magic = (int)val >> 3;
			else
				data41.magic = ((asmVer.Major << 3) | (asmVer.Minor << 2) | asmVer.Revision) - (int)val;
			return true;
		}

		static int getMagicArgIndex41Retail(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 3; i++) {
				var add = instrs[i];
				if (add.OpCode.Code != Code.Add)
					continue;
				var ldarg = instrs[i + 1];
				if (!DotNetUtils.isLdarg(ldarg))
					continue;
				var sub = instrs[i + 2];
				if (sub.OpCode.Code != Code.Sub)
					continue;
				var ldci4 = instrs[i + 3];
				if (!DotNetUtils.isLdcI4(ldci4) || DotNetUtils.getLdcI4Value(ldci4) != 0xFF)
					continue;

				return DotNetUtils.getArgIndex(ldarg);
			}
			return -1;
		}

		static int getMagicArgIndex41Trial(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldarg = instrs[i];
				if (!DotNetUtils.isLdarg(ldarg))
					continue;
				if (!DotNetUtils.isLdcI4(instrs[i + 1]))
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Shr)
					continue;

				return DotNetUtils.getArgIndex(ldarg);
			}
			return -1;
		}

		static FieldDefinition getLdtokenField(MethodDefinition method) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Ldtoken)
					continue;
				var field = instr.Operand as FieldDefinition;
				if (field == null || field.InitialValue == null || field.InitialValue.Length == 0)
					continue;

				return field;
			}
			return null;
		}

		static string[] handlerLocalTypes_V3 = new string[] {
			"System.AppDomain",
			"System.Byte[]",
			"System.Collections.Generic.Dictionary`2<System.String,System.String>",
			"System.IO.Compression.DeflateStream",
			"System.IO.MemoryStream",
			"System.IO.Stream",
			"System.Reflection.Assembly",
			"System.String",
			"System.String[]",
		};
		static bool checkHandlerV3(MethodDefinition handler) {
			return new LocalTypes(handler).all(handlerLocalTypes_V3);
		}

		static Data40 checkHandlerV40(MethodDefinition handler) {
			var data40 = new Data40();

			var instrs = handler.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = i;

				if (instrs[index++].OpCode.Code != Code.Ldarg_1)
					continue;

				var ldtoken = instrs[index++];
				if (ldtoken.OpCode.Code != Code.Ldtoken)
					continue;
				var field = ldtoken.Operand as FieldDefinition;

				string methodSig = "(System.ResolveEventArgs,System.RuntimeFieldHandle,System.Int32,System.String,System.Int32)";
				var method = ldtoken.Operand as MethodDefinition;
				if (method != null) {
					// >= 4.0.4
					if (!DotNetUtils.isMethod(method, "System.Byte[]", "()"))
						continue;
					field = getResourceField(method);
					methodSig = "(System.ResolveEventArgs,System.RuntimeMethodHandle,System.Int32,System.String,System.Int32)";
				}
				else {
					// 4.0.1.18 .. 4.0.3
				}

				if (field == null || field.InitialValue == null || field.InitialValue.Length == 0)
					continue;

				var ldci4_len = instrs[index++];
				if (!DotNetUtils.isLdcI4(ldci4_len))
					continue;
				if (DotNetUtils.getLdcI4Value(ldci4_len) != field.InitialValue.Length)
					continue;

				if (instrs[index++].OpCode.Code != Code.Ldstr)
					continue;

				var ldci4_magic = instrs[index++];
				if (!DotNetUtils.isLdcI4(ldci4_magic))
					continue;
				data40.magic = DotNetUtils.getLdcI4Value(ldci4_magic);

				var call = instrs[index++];
				if (call.OpCode.Code == Code.Tail)
					call = instrs[index++];
				if (call.OpCode.Code != Code.Call)
					continue;
				if (!DotNetUtils.isMethod(call.Operand as MethodReference, "System.Reflection.Assembly", methodSig))
					continue;

				data40.resourceField = field;
				data40.getDataMethod = method;
				return data40;
			}

			return null;
		}

		static FieldDefinition getResourceField(MethodDefinition method) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Ldtoken)
					continue;
				var field = instr.Operand as FieldDefinition;
				if (field == null || field.InitialValue == null || field.InitialValue.Length == 0)
					continue;
				return field;
			}
			return null;
		}

		public void initialize() {
			if (resolveHandler == null)
				return;

			if (version == ResourceVersion.V3) {
				simpleDeobfuscator.deobfuscate(resolveHandler);
				simpleDeobfuscator.decryptStrings(resolveHandler, deob);
				data30 = new Data30();
				data30.resource = DeobUtils.getEmbeddedResourceFromCodeStrings(module, resolveHandler);
				if (data30.resource == null) {
					Log.w("Could not find resource of encrypted resources");
					return;
				}
			}
		}

		public bool mergeResources(out EmbeddedResource rsrc) {
			rsrc = null;

			switch (version) {
			case ResourceVersion.V3:
				if (data30.resource == null)
					return false;

				DeobUtils.decryptAndAddResources(module, data30.resource.Name, () => decryptResourceV3(data30.resource));
				rsrc = data30.resource;
				return true;

			case ResourceVersion.V40:
				return decryptResource(data40.resourceField, data40.magic);

			case ResourceVersion.V41:
				return decryptResource(data41.resourceField, data41.magic);

			default:
				return true;
			}
		}

		bool decryptResource(FieldDefinition resourceField, int magic) {
			if (resourceField == null)
				return false;

			string name = string.Format("Embedded data field {0:X8} RVA {1:X8}", resourceField.MetadataToken.ToInt32(), resourceField.RVA);
			DeobUtils.decryptAndAddResources(module, name, () => decryptResourceV4(resourceField.InitialValue, magic));
			resourceField.InitialValue = new byte[1];
			resourceField.FieldType = module.TypeSystem.Byte;
			return true;
		}
	}
}
