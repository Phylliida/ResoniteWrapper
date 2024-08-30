﻿using HarmonyLib; // HarmonyLib comes included with a ResoniteModLoader install
using ResoniteModLoader;
using System;
using System.Reflection;
using ProtoFlux.Core;
//using ProtoFlux.Runtimes.Execution;
using System.Runtime.CompilerServices;
using Elements.Core;
using ProtoFluxBindings;
using Wasmtime;
using FrooxEngine;
using SkyFrost.Base;
using System.Security.Policy;
using System.Collections.Generic;
using ResoniteHotReloadLib;
using System.ComponentModel;
using System.Net;
//using ProtoFlux.Runtimes.Execution.Nodes;
using FrooxEngine.ProtoFlux;
//using ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Variables;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Network;
using System.Configuration;
using ProtoFlux.Runtimes.Execution;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Async;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
//using ProtoFlux.Runtimes.Execution.Nodes;
// tmp

namespace ResoniteWrapper
{
    public class SimpleMod
    {
        public static void Bees(string wow, int bees, float ok, Slot item, 
            out Dictionary<String, String> out1, out float out2, out Grabbable out3)
        {
            out1 = new Dictionary<string, string>();
            if (wow != null)
            {
                out1[wow] = bees.ToString();
            }
            out2 = ok;
            out3 = null;
            if (item != null)
            {
                out3 = item.GetComponent<Grabbable>();
            }
        }

        public static string readFromDict(Dictionary<string, string> dict, string key)
        {
            return dict[key];
        }
    }


    public class ResoniteWrapper : ResoniteMod
    {
        static string RESONITE_WRAPPER_PATH = "Generate Wrapper Flux";

        static string MOD_PREFIX = "mod://";

        public enum VariableKind
        {
            Parameter,
            TupleFromReturn,
            ReturnValue
        }

        public struct VariableInfo
        {
            public Type type;
            public string name;
            public VariableKind variableKind;
            public int paramIndex;
            public bool isValidResoniteType;
            public bool isResoniteTypeValueType;
            public Type resoniteType;


            static bool isTypeValidResoniteType(Type type, out bool isValueType)
            {
                try
                {
                    Msg("testing type " + type);
                    // extra checks internal to resonite
                    bool result = typeof(DynamicValueVariable<>).MakeGenericType(type).IsValidGenericType(validForInstantiation: true) ||
                     typeof(DynamicReferenceVariable<>).MakeGenericType(type).IsValidGenericType(validForInstantiation: true);

                    isValueType = typeof(DynamicValueVariable<>).MakeGenericType(type).IsValidGenericType(validForInstantiation: true) && type != typeof(System.String);
                    Msg("got result " + result);
                    return result;

                }
                catch (ArgumentException) // happens if invalid type
                {
                    isValueType = false;
                    Msg("got exception for type " + type);
                    return false;
                }
            }

            public VariableInfo(string name, Type type, VariableKind variableKind, int paramIndex = 0)
            {
                this.name = name;
                this.type = type;
                this.variableKind = variableKind;
                this.paramIndex = paramIndex;
                this.isValidResoniteType = isTypeValidResoniteType(type, out this.isResoniteTypeValueType);
                this.resoniteType = this.isValidResoniteType ? type : typeof(string);
            }
        }

        public class RollingCache
        {
            public int offset;
            public System.Object[] values;
            public Guid[] keys;

            public RollingCache(int capacity)
            {
                values = new object[capacity];
                keys = new Guid[capacity];
            }

            public bool TryLookup(System.Object value, out Guid guid)
            {
                guid = Guid.Empty;
                for (int i = 0; i < values.Length; i++ )
                {
                    if (values[i] == value)
                    {
                        guid = keys[i];
                        return true;
                    }
                }
                return false;
            }

            public void Add(System.Object value, Guid key)
            {
                values[offset] = value;
                keys[offset] = key;
                offset = (offset + 1) % values.Length;
            }
        }

        public class UnsupportedTypeLookup
        {
            // maintain a cache/lookup for each type, this ensures caches don't get flooded out by other types
            // and so prevents entry creation spam
            int cacheCapacity;
            Dictionary<Type, UnsupportedTypeLookupHelper> lookups = new Dictionary<Type, UnsupportedTypeLookupHelper>();
            
            public UnsupportedTypeLookup(int cacheCapacity)
            {
                this.cacheCapacity = cacheCapacity;
            }

            UnsupportedTypeLookupHelper GetHelperForType(Type type)
            {
                UnsupportedTypeLookupHelper lookup;
                if (!lookups.TryGetValue(type, out lookup))
                {
                    lookup = new UnsupportedTypeLookupHelper(cacheCapacity);
                    lookups[type] = lookup;
                }
                return lookup;
            }

            public Guid Add(System.Object value)
            {
                if (value == null)
                {
                    return Guid.Empty;
                }
                return GetHelperForType(value.GetType()).Add(value);
            }

            public bool TryGet(Guid guid, Type type, out System.Object value)
            {
                if (guid == Guid.Empty)
                {
                    value = null;
                    return true;
                }
                return GetHelperForType(type).TryGet(guid, out value);
            }
        }

        public class UnsupportedTypeLookupHelper
        {
            RollingCache cache;
            public Dictionary<Guid, System.Object> lookup = new Dictionary<Guid, object>();

            public UnsupportedTypeLookupHelper(int cacheCapacity)
            {
                cache = new RollingCache(cacheCapacity);
            }

            public Guid Add(System.Object value)
            {
                Guid guid;
                // we have a cache so the first 10 or so values won't allocate new dict entries every call
                if (!cache.TryLookup(value, out guid))
                {
                    guid = Guid.NewGuid();
                    while (lookup.ContainsKey(guid) || guid == Guid.Empty)
                    {
                        guid = Guid.NewGuid();
                    }
                    lookup[guid] = value;
                    cache.Add(value, guid);
                }
                return guid;
            }

            public bool TryGet(Guid guid, out System.Object value)
            {
                if (guid == Guid.Empty) // empty guid is null guid
                {
                    value = null;
                    return true;
                }
                return lookup.TryGetValue(guid, out value);
            }
        }

        public static void GetMethodVars(MethodInfo method, out List<VariableInfo> inputVars, out List<VariableInfo> returnVars)
        {
            inputVars = new List<VariableInfo>();
            returnVars = new List<VariableInfo>();
            // Modified from https://stackoverflow.com/a/28772413
            Type returnType = method.ReturnType;
            Msg(returnType.ToString());
            if (returnType.IsGenericType)
            {
                var genType = returnType.GetGenericTypeDefinition();
                if (genType == typeof(Tuple<>)
                    || genType == typeof(Tuple<,>)
                    || genType == typeof(Tuple<,,>)
                    || genType == typeof(Tuple<,,,>)
                    || genType == typeof(Tuple<,,,,>)
                    || genType == typeof(Tuple<,,,,,>)
                    || genType == typeof(Tuple<,,,,,,>)
                    || genType == typeof(Tuple<,,,,,,,>))
                {
                    for (var i = 0; i < genType.GetGenericArguments().Length; i++)
                    {
                        Msg(genType.GetGenericArguments()[i].ToString() + "arg");
                        returnVars.Add(new VariableInfo(i.ToString(), genType.GetGenericArguments()[i], VariableKind.TupleFromReturn, i));
                    }
                }
                else
                {
                    returnVars.Add(new VariableInfo("0", returnType, VariableKind.ReturnValue));
                }
            }
            else if(returnType != typeof(void)) // don't use void as a type
            {
                returnVars.Add(new VariableInfo("0", returnType, VariableKind.ReturnValue));
            }

            ParameterInfo[] methodParams = method.GetParameters();

            for (int i = 0; i < methodParams.Length; i++)
            {
                ParameterInfo param = methodParams[i];
                Msg(param.Name + " " + param.ParameterType);
                // out type
                if (param.ParameterType.IsByRef && param.IsOut)
                {
                    // GetElementType is needed for byref types otherwise they have a & at the end and it confuses things
                    returnVars.Add(new VariableInfo(param.Name, param.ParameterType.GetElementType(), VariableKind.Parameter, paramIndex: i));
                }
                // ref type, its an input and output
                else if (param.ParameterType.IsByRef && !param.IsOut)
                {
                    inputVars.Add(new VariableInfo(param.Name, param.ParameterType.GetElementType(), VariableKind.Parameter, paramIndex: i));
                    returnVars.Add(new VariableInfo(param.Name, param.ParameterType.GetElementType(), VariableKind.Parameter, paramIndex: i));
                }
                // input type
                else if (!param.IsOut)
                {
                    inputVars.Add(new VariableInfo(param.Name, param.ParameterType, VariableKind.Parameter, paramIndex: i));
                }
            }
        }
        public class WrappedMethod : IDisposable
        {
            MethodInfo method;
            string name;
            string modNamespace;
            List<VariableInfo> inputVars;
            List<VariableInfo> returnVars;

            public WrappedMethod(MethodInfo method, string modNamespace)
            {
                this.method = method;
                this.name = method.Name;
                this.modNamespace = modNamespace;
                GetMethodVars(method, out inputVars, out returnVars);
                AddReloadMenuOption();
            }

            public void Dispose()
            {
                Msg("Cleaning up " + GetUri());
                RemoveMenuOption();
            }

            public void CallMethod(Slot dataSlot, UnsupportedTypeLookup typeLookup)
            {
                bool success = true;
                string error = "";
                DynamicVariableSpace space = dataSlot.GetComponent<DynamicVariableSpace>();
                if (space == null)
                {
                    return;
                }
                Object[] parameters = new object[method.GetParameters().Length];
                object[] dummyParams = new object[2] { null, null };
                foreach (VariableInfo inputVar in inputVars)
                {
                    Msg("getting input var " + inputVar.resoniteType + " " + inputVar.name);
                    // Cursed stuff to call of generic type
                    MethodInfo readMethod = typeof(DynamicVariableSpace).GetMethod(nameof(space.TryReadValue));
                    MethodInfo genericReadMethod = readMethod.MakeGenericMethod(inputVar.type);
                    // the null at second place works as an out
                    dummyParams[0] = inputVar.name;
                    dummyParams[1] = null;
                    bool found = (bool)genericReadMethod.Invoke(space, dummyParams);
                    object value = dummyParams[1];
                    if (found)
                    {
                        Msg("got input var " + inputVar.resoniteType + " " + inputVar.name);
                        if (inputVar.isValidResoniteType)
                        {
                            parameters[inputVar.paramIndex] = value;
                        }
                        else
                        {
                            // if not a valid resonite type we need to use our lookup table to find the value
                            // because we just store a string with a guid pointing to it
                            System.Object nonResoniteValue;
                            Guid inputParamGuid;
                            if(Guid.TryParse((string)value, out inputParamGuid) &&
                                typeLookup.TryGet(inputParamGuid, inputVar.type, out nonResoniteValue)) {
                                parameters[inputVar.paramIndex] = nonResoniteValue;
                                Msg("read of type " + inputVar.resoniteType + " " + inputVar.name);
                            }
                            else
                            {
                                success = false;
                                error = "Failed to read parameter " + inputVar.name + " with non resonite type " + inputVar.type + " the guid of " + inputParamGuid + " does not exist in lookup";
                            }
                        }
                    }
                    else
                    {
                        error = "In Dynvar, could not find parameter with name " + inputVar.name + " with type " + inputVar.resoniteType;
                        success = false;
                        break;
                    }
                }

                if (success)
                {
                    Msg("calling method " + method.Name);

                    // null for first input to Invoke means static, we only support static methods
                    var result = this.method.Invoke(null, parameters);

                    Msg("called method " + method.Name);

                    foreach (VariableInfo returnVar in returnVars)
                    {
                        Msg("starting on var " + returnVar.name + " with type " + returnVar.type.ToString());
                        object value = null;

                        if (returnVar.variableKind == VariableKind.Parameter)
                        {
                            value = parameters[returnVar.paramIndex];
                        }
                        else if(returnVar.variableKind == VariableKind.ReturnValue)
                        {
                            value = result;
                        }
                        else if(returnVar.variableKind == VariableKind.TupleFromReturn)
                        {
                            if (result == null)
                            {
                                success = false;
                                error = "Expected tuple, returned null";
                            }
                            else
                            {
                                ITuple resultTuple = result as ITuple;

                                Msg("casted tuple " + method.Name);
                                if (resultTuple == null)
                                {
                                    success = false;
                                    error = "Expected tuple, returned value of type " + result.GetType().ToString();
                                }
                                else
                                {
                                    Msg("reading from tuple " + returnVar.name + " " + returnVar.paramIndex + " " + returnVar.type);
                                    value = resultTuple[returnVar.paramIndex];
                                }
                            }
                        }
                        if (success)
                        {
                            Msg("return var of type " + returnVar.type.ToString() + " is valid resonite type? " + returnVar.isValidResoniteType);
                            // if not a valid resonite type we need to convert to string using our lookup table
                            if (!returnVar.isValidResoniteType)
                            {
                                Msg("adding to table");
                                // We don't want to allocate a new uuid if we are just passing through a value or returning a constant value
                                // this has a small cache for each type encountered to prevent that
                                value = typeLookup.Add(value).ToString();
                                Msg("got uuid " + value);
                            }
                            if (value != null)
                            {
                                Msg("Got value of type " + value.GetType().ToString());
                            }
                            else
                            {
                                Msg("Got null value");
                            }
                            //DynamicVariableWriteResult writeResult = space.TryWriteValue(returnVar.name, value);
                            //Msg("did write");
                            // Cursed stuff to call of generic type
                            MethodInfo writeMethod = typeof(DynamicVariableSpace).GetMethod(nameof(space.TryWriteValue));
                            MethodInfo genericWriteMethod = writeMethod.MakeGenericMethod(returnVar.resoniteType);
                            dummyParams[0] = returnVar.name;
                            dummyParams[1] = value;
                            DynamicVariableWriteResult writeResult = (DynamicVariableWriteResult)genericWriteMethod.Invoke(space, dummyParams);

                            Msg("write result of " + writeResult);
                            switch (writeResult)
                            {
                                case DynamicVariableWriteResult.Success:
                                    break;
                                case DynamicVariableWriteResult.NotFound:
                                    success = false;
                                    error = "When writing, could not find output variable " + returnVar.name + " of type " + returnVar.type.ToString() + " and resonite type " + returnVar.resoniteType.ToString();
                                    break;
                                case DynamicVariableWriteResult.Failed:
                                    success = false;
                                    error = "When writing, could write to output variable " + returnVar.name + " of type " + returnVar.type.ToString() + " and resonite type " + returnVar.resoniteType.ToString();
                                    break;
                            }
                        }                        
                        if (!success)
                        {
                            break;
                        }
                    }
                }
                if (!success)
                {
                    Msg("Writing error " + error);
                    space.TryWriteValue("error", error);
                }
                Msg("Done running method " + name);
            }

            Slot CreateEmptySlot(string name)
            {
                return FrooxEngine.Engine.Current.WorldManager.FocusedWorld.LocalUserSpace.AddSlot(name);
            }
            void CreateVarsForVar(Slot slot, string spaceName, VariableInfo var)
            {
                Type dynvarType;
                // DynamicValueVariables are weird like this and string is value but everywhere else is a object
                if (var.isResoniteTypeValueType || var.resoniteType == typeof(System.String))
                {
                    dynvarType = typeof(DynamicValueVariable<>).MakeGenericType(var.resoniteType);
                }
                else
                {
                    dynvarType = typeof(DynamicReferenceVariable<>).MakeGenericType(var.resoniteType);
                }

                Msg("now type is valid" + dynvarType.IsValidGenericType(validForInstantiation: true));
                Msg("starting");
                Msg("Slot" + slot.ReferenceID + " space name " + spaceName + " var " + var.name + " type " + var.resoniteType);
                Msg(" atta Slot" + slot.ReferenceID + " space name " + spaceName + " var " + var.name + " type " + var.resoniteType);
                var attached = slot.AttachComponent(dynvarType);
                Msg("sSlot" + attached + " bb " + slot.ReferenceID + " space name " + spaceName + " var " + var.name + " type " + var.resoniteType);                

                FieldInfo field = attached.GetType().GetField("VariableName");
                Msg("field " + field);

                Sync<string> variableName = (Sync<string>)field.GetValue(attached);
                variableName.Value = spaceName + "/" + var.name;
                Msg("done write");
                //Msg("field get " + field.GetValue(attached).GetType());
                //Msg("field get direct " + field.GetValueDirect(typedR));
                // weird stuff to let us write to a field even tho we don't know generic type
                // var syncField = attached.GetType().GetField("VariableName").GetValueDirect(attached);
                // Msg("b " + syncField + " slot " + slot.ReferenceID + " space name " + spaceName + " var " + var.name + " type " + var.resoniteType);

                // typeof(Sync<String>).GetField("Value").SetValue(syncField, spaceName + "/" + var.name);
            }

            public Uri GetUri()
            {
                return new Uri(MOD_PREFIX + modNamespace + "/" + method.Name);
            }

            public Slot CreateTemplate(Slot holder)
            {
                Slot template = holder.AddSlot("ParameterTemplate");
                DynamicVariableSpace space = template.AttachComponent<DynamicVariableSpace>();
                space.SpaceName.Value = method.Name;
                space.OnlyDirectBinding.Value = true;
                foreach (VariableInfo varInfo in inputVars)
                {
                    CreateVarsForVar(template, method.Name, varInfo);
                }
                foreach (VariableInfo varInfo in returnVars)
                {
                    CreateVarsForVar(template, method.Name, varInfo);
                }
                WebsocketClient client = template.AttachComponent<WebsocketClient>();
                client.URL.Value = GetUri();

                DynamicReferenceVariable<WebsocketClient> wsVar = template.AttachComponent<DynamicReferenceVariable<WebsocketClient>>();
                wsVar.VariableName.Value = method.Name + "/" + "FAKE_WS_CLIENT";
                wsVar.Reference.Value = client.ReferenceID;

                DynamicValueVariable<string> errorMessage = template.AttachComponent<DynamicValueVariable<string>>();
                errorMessage.VariableName.Value = method.Name + "/" + "error";
                errorMessage.Value.Value = "";

                return template;
            }

            public T CreateSlotWithComponent<T>(Slot parent, string name, float3 pos, Slot addToSlot) where T : FrooxEngine.Component
            {
                if (addToSlot == null)
                {
                    addToSlot = parent.AddSlot(name);
                    addToSlot.Position_Field.Value = pos;
                }
                return (T)addToSlot.AttachComponent(typeof(T));
            }

            public Slot CreateFlux(bool monopack)
            {
                Slot addToSlot = null;
                Slot holder = CreateEmptySlot(method.Name);
                Slot template = CreateTemplate(holder);

                if (monopack)
                {
                    addToSlot = holder.AddSlot("Monopacked flux");
                }
                AsyncCallRelay relay = CreateSlotWithComponent<AsyncCallRelay>(holder, "AsyncCallRelay:Call", new float3(-0.5f, 0.19f, 0), null);
                
                RefObjectInput<Slot> templateInput = CreateSlotWithComponent<RefObjectInput<Slot>>(holder, "RefObjectInput`1", new float3(-0.28f, 0.37f, 0.02f), addToSlot);
                templateInput.Target.Value = template.ReferenceID;

                ReadDynamicObjectVariable<WebsocketClient> wsClientVar = CreateSlotWithComponent<ReadDynamicObjectVariable<WebsocketClient>>(holder, "ReadDynamicObjectVariable`1", new float3(0, 0.3f, 0), addToSlot);
                ValueObjectInput<string> wsClientId = CreateSlotWithComponent<ValueObjectInput<string>>(holder, "ValueObjectInput`1", new float3(-0.223f, 0.29f, 0), addToSlot);
                wsClientId.Value.Value = method.Name + "/" + "FAKE_WS_CLIENT";
                wsClientVar.Source.Value = templateInput.ReferenceID;
                wsClientVar.Path.Value = wsClientId.ReferenceID;

                WebsocketTextMessageSender sender = CreateSlotWithComponent<WebsocketTextMessageSender>(holder, "WebsocketTextMessageSender", new float3(0.2f, 0.28f, 0), addToSlot);
                sender.Client.Value = wsClientVar.Value.ReferenceID;

                SyncRef<INodeOperation> curNode = relay.OnTriggered;

                float3 offset = new float3(0, -0.17f, 0);
                float3 curOffset = wsClientVar.Slot.LocalPosition;
                foreach (VariableInfo inputVar in inputVars)
                {
                    curOffset += offset;
                    Type writeDynvarType;
                    string writeName;
                    Msg("starting with type " + inputVar.resoniteType.ToString() + " with name " + inputVar.name);
                    if (inputVar.isResoniteTypeValueType)
                    {
                        writeDynvarType = typeof(WriteDynamicValueVariable<>).MakeGenericType(inputVar.resoniteType);
                        writeName = "WriteDynamicValueVariable`1";
                    }
                    else
                    {
                        writeDynvarType = typeof(WriteDynamicObjectVariable<>).MakeGenericType(inputVar.resoniteType);
                        writeName = "WriteDynamicObjectVariable`1";
                    }
                    Msg("dynvar");
                    Slot inputVarSlot = addToSlot;
                    if (inputVarSlot == null)
                    {
                        inputVarSlot = holder.AddSlot(writeName);
                        inputVarSlot.Position_Field.Value = curOffset;
                    }
                    var attachedInputVarWriter = inputVarSlot.AttachComponent(writeDynvarType);

                    Msg("Attached");
                    // Write to Target (uses field holding template)
                    var targetField = attachedInputVarWriter.GetType().GetField("Target").GetValue(attachedInputVarWriter);
                    targetField.GetType().GetProperty("Value").SetValue(targetField, templateInput.ReferenceID);

                    Msg("Target");
                    // Create Path field and write to Path
                    ValueObjectInput<string> inputVarId = CreateSlotWithComponent<ValueObjectInput<string>>(holder, "ValueObjectInput`1", curOffset + new float3(-0.223f, -0.01f, -0.01f), addToSlot);
                    inputVarId.Value.Value = method.Name + "/" + inputVar.name;
                    var pathField = attachedInputVarWriter.GetType().GetField("Path").GetValue(attachedInputVarWriter);
                    pathField.GetType().GetProperty("Value").SetValue(pathField, inputVarId.ReferenceID);
                    Msg("Relay");

                    // Create Relay
                    Type relayType;
                    if (inputVar.isResoniteTypeValueType)
                    {
                        relayType = typeof(ValueRelay<>).MakeGenericType(inputVar.resoniteType);
                    }
                    else
                    {
                        relayType = typeof(ObjectRelay<>).MakeGenericType(inputVar.resoniteType);
                    }
                    Slot relaySlot = addToSlot;
                    if (relaySlot == null)
                    {
                        relaySlot = holder.AddSlot("Relay:" + inputVar.name);
                        relaySlot.Position_Field.Value = curOffset + new float3(-0.5f, -0.07f, 0);
                    }
                    var relayComponent = relaySlot.AttachComponent(relayType);
                    var relayRefId = relayComponent.GetType().GetProperty("ReferenceID").GetValue(relayComponent);
                    Msg("relay made");

                    // Write relay to Value
                    var valueField = attachedInputVarWriter.GetType().GetField("Value").GetValue(attachedInputVarWriter);
                    valueField.GetType().GetProperty("Value").SetValue(valueField, relayRefId);
                    Msg("relay write");

                    curNode.Value = (RefID)attachedInputVarWriter.GetType().GetProperty("ReferenceID").GetValue(attachedInputVarWriter);
                    curNode = (SyncRef<INodeOperation>)attachedInputVarWriter.GetType().GetField("OnSuccess").GetValue(attachedInputVarWriter);
                }

                curNode.Value = sender.ReferenceID;

                curNode = sender.OnSent;

                offset = new float3(0, -0.17f, 0);
                curOffset = wsClientVar.Slot.LocalPosition + new float3(0.5f, 0, 0);
                foreach (VariableInfo returnVar in returnVars)
                {
                    curOffset += offset;
                    Type readDynvarType;
                    string readName;
                    Msg("starting with type " + returnVar.resoniteType.ToString() + " with name " + returnVar.name);
                    if (returnVar.isResoniteTypeValueType)
                    {
                        readDynvarType = typeof(ReadDynamicValueVariable<>).MakeGenericType(returnVar.resoniteType);
                        readName = "ReadDynamicValueVariable`1";
                    }
                    else
                    {
                        readDynvarType = typeof(ReadDynamicObjectVariable<>).MakeGenericType(returnVar.resoniteType);
                        readName = "ReadDynamicObjectVariable`1";
                    }
                    Msg("dynvar");
                    Slot returnVarSlot = addToSlot;
                    if (returnVarSlot == null)
                    {
                        returnVarSlot = holder.AddSlot(readName);
                        returnVarSlot.Position_Field.Value = curOffset+new float3(0, 0.0025f, 0);
                    }
                    var attachedReturnVarReader = returnVarSlot.AttachComponent(readDynvarType);


                    Msg("Attached");
                    // Write to Source (uses field holding template)
                    var sourceField = attachedReturnVarReader.GetType().GetField("Source").GetValue(attachedReturnVarReader);
                    sourceField.GetType().GetProperty("Value").SetValue(sourceField, templateInput.ReferenceID);

                    Msg("Source");
                    // Create Path field and write to Path
                    ValueObjectInput<string> returnVarId = CreateSlotWithComponent<ValueObjectInput<string>>(holder, "ValueObjectInput`1", curOffset + new float3(-0.223f, -0.01f, -0.01f), addToSlot);
                    returnVarId.Value.Value = method.Name + "/" + returnVar.name;
                    var pathField = attachedReturnVarReader.GetType().GetField("Path").GetValue(attachedReturnVarReader);
                    pathField.GetType().GetProperty("Value").SetValue(pathField, returnVarId.ReferenceID);
                    Msg("Relay");

                    // Create Write
                    Type writeType = returnVar.isResoniteTypeValueType ?
                        typeof(ValueWrite<>).MakeGenericType(returnVar.resoniteType) :
                        typeof(ObjectWrite<>).MakeGenericType(returnVar.resoniteType);
                    string writeName = (returnVar.isResoniteTypeValueType ?
                        "Value" : "Object") + "Write`1";

                    Slot writeSlot = addToSlot;
                    if (writeSlot == null)
                    {
                        writeSlot = holder.AddSlot(writeName);
                        writeSlot.Position_Field.Value = curOffset + new float3(0.15f, -0.005f,0);
                    }

                    var writeComponent = writeSlot.AttachComponent(writeType);

                    // Attach DynvarValue -> Write Value
                    var writeValueField = writeComponent.GetType().GetField("Value").GetValue(writeComponent);
                    var dynvarReadValueRefIdField = attachedReturnVarReader.GetType().GetField("Value").GetValue(attachedReturnVarReader);
                    var dynvarReadValueRefId = (RefID)dynvarReadValueRefIdField.GetType().GetProperty("ReferenceID").GetValue(dynvarReadValueRefIdField);
                    writeValueField.GetType().GetProperty("Value").SetValue(writeValueField, dynvarReadValueRefId);

                    curNode.Value = (RefID)writeComponent.GetType().GetProperty("ReferenceID").GetValue(writeComponent);
                    curNode = (SyncRef<INodeOperation>)writeComponent.GetType().GetField("OnWritten").GetValue(writeComponent);

                    // Create local
                    Type localType = returnVar.isResoniteTypeValueType ?
                        typeof(LocalValue<>).MakeGenericType(returnVar.resoniteType) :
                        typeof(LocalObject<>).MakeGenericType(returnVar.resoniteType);
                    String localName = returnVar.isResoniteTypeValueType ?
                        "LocalValue`1" :
                        "LocalObject`1";

                    Slot localSlot = addToSlot;
                    if (localSlot == null)
                    {
                        localSlot = holder.AddSlot(writeName);
                        localSlot.Position_Field.Value = curOffset + new float3(0.35f, 0, -0.01f);
                        localSlot.Name = returnVar.name;
                    }

                    // Write to Local
                    var localComponent = localSlot.AttachComponent(localType);
                    var localComponentRefId = localComponent.GetType().GetProperty("ReferenceID").GetValue(localComponent);
                    var writeVarField = writeComponent.GetType().GetField("Variable").GetValue(writeComponent);
                    writeVarField.GetType().GetProperty("Value").SetValue(writeVarField, localComponentRefId);
                    
                    // Create Relay
                    Type relayType = returnVar.isResoniteTypeValueType ?
                        typeof(ValueRelay<>).MakeGenericType(returnVar.resoniteType) :
                        typeof(ObjectRelay<>).MakeGenericType(returnVar.resoniteType);

                    
                    Slot relaySlot = holder.AddSlot("Relay:" + returnVar.name);
                    relaySlot.Position_Field.Value = curOffset + new float3(0.5f, 0, 0);
                    
                    var relayComponent = relaySlot.AttachComponent(relayType);

                    // Connect Local Value To Relay
                    var relayInputField = relayComponent.GetType().GetField("Input").GetValue(relayComponent);
                    relayInputField.GetType().GetProperty("Value").SetValue(relayInputField, localComponentRefId);
                }

                AsyncCallRelay outrelay = CreateSlotWithComponent<AsyncCallRelay>(holder, "AsyncCallRelay:OnDone", new float3(1f, 0.3f, 0), null);

                curNode.Value = outrelay.ReferenceID;



                Slot headSlot = FrooxEngine.Engine.Current.WorldManager.FocusedWorld.LocalUser.GetBodyNodeSlot(BodyNode.Head);
                if (headSlot != null)
                {
                    holder.GlobalPosition = headSlot.GlobalPosition;
                }

                if (!monopack)
                {
                    holder.UnpackNodes();
                }

                return holder;
            }

            public string GetLabelString()
            {
                return modNamespace + "/" + name;
            }

            public void AddReloadMenuOption()
            {
                // modified from https://github.com/Nytra/ResoniteHotReloadLib/blob/11bc8c4167387d75fda0eed07237fee5424cb33c/HotReloader.cs#L344
                Msg("Begin Add Generate Flux Menu Option, for " + modNamespace + " for function " + name);
                if (!FrooxEngine.Engine.Current.IsInitialized)
                {
                    FrooxEngine.Engine.Current.RunPostInit(AddActionDelegate);
                }
                else
                {
                    AddActionDelegate();
                }
                void AddActionDelegate()
                {
                    DevCreateNewForm.AddAction(RESONITE_WRAPPER_PATH, GetLabelString(), (x) =>
                    {
                        x.Destroy();

                        Msg("Pressed generate flux for " + GetLabelString());

                        Slot result = CreateFlux(false);
                    });
                }
            }

            public bool RemoveMenuOption()
            {
                Msg("Begin RemoveMenuOption");
                object categoryNode = AccessTools.Field(typeof(DevCreateNewForm), "root").GetValue(null);
                object subcategory = AccessTools.Method(categoryNode.GetType(), "GetSubcategory").Invoke(categoryNode, new object[] { RESONITE_WRAPPER_PATH });
                System.Collections.IList elements = (System.Collections.IList)AccessTools.Field(categoryNode.GetType(), "_elements").GetValue(subcategory);
                if (elements == null)
                {
                    Msg("Elements is null!");
                    return false;
                }
                foreach (object categoryItem in elements)
                {
                    var name = (string)AccessTools.Field(categoryNode.GetType().GetGenericArguments()[0], "name").GetValue(categoryItem);
                    //var action = (Action<Slot>)AccessTools.Field(categoryItemType, "action").GetValue(categoryItem);
                    if (name == GetLabelString())
                    {
                        elements.Remove(categoryItem);
                        Msg("Menu option removed for " + GetLabelString());
                        return true;
                    }
                }
                return false;
            }
        }

        public static List<bool> GetSupportedTypes(List<Type> types)
        {
            List<bool> supported = new List<bool>();
            foreach (Type type in types)
            {
                supported.Add(FrooxEngine.Engine.Current.WorldManager.FocusedWorld.Types.IsSupported(type));
            }
            return supported;
        }

        public static int CACHE_CAPACITY = 20;

        public static UnsupportedTypeLookup globalTypeLookup = new UnsupportedTypeLookup(CACHE_CAPACITY);

        public static Dictionary<Uri, WrappedMethod> wrappedMethods = new Dictionary<Uri, WrappedMethod>();

        public static Dictionary<Tuple<string, string>, List<Uri>> wrappedMethodLookup = new Dictionary<Tuple<string, string>, List<Uri>>();

        public static void WrapClass(Type classType, string modNamespace)
        {
            List<Uri> methodUris = new List<Uri>();
            foreach (MethodInfo method in classType.GetMethods())
            {
                if (method.IsStatic && method.IsPublic)
                {
                    WrappedMethod wrappedMethod = new WrappedMethod(method, modNamespace + "/" + classType.ToString());
                    wrappedMethods[wrappedMethod.GetUri()] = wrappedMethod;
                    methodUris.Add(wrappedMethod.GetUri());
                }
            }
            wrappedMethodLookup[new Tuple<string, string>(classType.ToString(), modNamespace)] = methodUris;
        }

        public static void UnwrapClass(Type classType, string modNamespace)
        {
            Tuple<string, string> key = new Tuple<string, string>(classType.ToString(), modNamespace);
            if (wrappedMethodLookup.ContainsKey(key))
            {
                List<Uri> uris = wrappedMethodLookup[key];
                foreach (Uri uri in uris)
                {
                    if (wrappedMethods.ContainsKey(uri))
                    {
                        WrappedMethod wrappedMethod = wrappedMethods[uri];
                        if (wrappedMethod != null)
                        {
                            wrappedMethod.Dispose();
                        }
                        else
                        {
                            Warn("Wrapped method is null?? with uri " + uri);
                        }
                        wrappedMethods.Remove(uri);
                    }
                }
                wrappedMethodLookup.Remove(key);
            }
            else
            {
                Warn("Tried to clean up class with type " + classType.ToString() + " and namespace " + modNamespace + " but did not exist, did you create it?");
            }
        }

        public override string Name => "ResoniteWrapper";
        public override string Author => "TessaCoil";
        public override string Version => "1.0.0"; //Version of the mod, should match the AssemblyVersion
        public override string Link => "https://github.com/Phylliida/ResoniteWrapper"; // Optional link to a repo where this mod would be located

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> enabled = new ModConfigurationKey<bool>("enabled", "Should the mod be enabled", () => true); //Optional config settings

        private static ModConfiguration Config; //If you use config settings, this will be where you interface with them.
        private static string harmony_id = "bepis.Phylliida.ResoniteWrapper";

        private static Harmony harmony;
        public override void OnEngineInit()
        {
            HotReloader.RegisterForHotReload(this);

            Config = GetConfiguration(); //Get the current ModConfiguration for this mod
            Config.Save(true); //If you'd like to save the default config values to file
        
            SetupMod();
        }

        public static void SetupMod()
        {
            harmony = new Harmony(harmony_id); //typically a reverse domain name is used here (https://en.wikipedia.org/wiki/Reverse_domain_name_notation)
            harmony.PatchAll(); // do whatever LibHarmony patching you need, this will patch all [HarmonyPatch()] instances
          
            Msg("applied resonite wrapper patches successfully");

            WrapClass(typeof(SimpleMod), "simplemod");
        }

        static void CleanupAllWrappedFunctions()
        {
            foreach (KeyValuePair<Uri, WrappedMethod> wrapped in wrappedMethods)
            {
                wrapped.Value.Dispose();
            }
            wrappedMethods.Clear();
            wrappedMethodLookup.Clear();
        }

        static void BeforeHotReload()
        {
            harmony = new Harmony(harmony_id);
            // This runs in the current assembly (i.e. the assembly which invokes the Hot Reload)
            harmony.UnpatchAll();
            
            // This is where you unload your mod, free up memory, and remove Harmony patches etc.
            Msg("Cleaning up functions");
            CleanupAllWrappedFunctions();
            Msg("Cleaned");
        }

        static void OnHotReload(ResoniteMod modInstance)
        {
            // This runs in the new assembly (i.e. the one which was loaded fresh for the Hot Reload)
            
            // Get the config
            Config = modInstance.GetConfiguration();

            // Now you can setup your mod again
            SetupMod();
        }

        [HarmonyPatch(typeof(ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Network.WebsocketTextMessageSender), "RunAsync")]
        class WebsocketSendPatch
        {
            [HarmonyPostfix]
            static async Task<IOperation> PostfixBoiii(Task<IOperation> __result, ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Network.WebsocketTextMessageSender __instance, System.Object __state)
            {
                if (__state == null)
                {
                    if (__result != null)
                    {
                        return __result.Result;
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return __instance.OnSent.Target;
                }
            }

            [HarmonyPrefix]
            static bool Prefix(ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Network.WebsocketTextMessageSender __instance, FrooxEngineContext context, ref object __state)
            {
                __state = null;
                WebsocketClient websocketClient = __instance.Client.Evaluate(context);
                if (websocketClient != null && websocketClient.URL.Value != null)
                {
                    if (websocketClient.URL.Value.ToString().StartsWith(MOD_PREFIX))
                    {
                        Msg("Got websocket with url " + websocketClient.URL);
                        if (wrappedMethods.ContainsKey(websocketClient.URL))
                        {
                            wrappedMethods[websocketClient.URL].CallMethod(websocketClient.Slot, globalTypeLookup);
                            __state = context;
                            return false;
                        }
                    }
                }
                
                return true;
            }
        }
    }
}
