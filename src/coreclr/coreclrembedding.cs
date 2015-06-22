using System;
using System.Security;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Dynamic;
using System.Collections.Generic;
using System.Collections;

[StructLayout(LayoutKind.Sequential)]
public struct JsObjectData
{
	public int propertiesCount;
	public IntPtr propertyTypes;
	public IntPtr propertyNames;
	public IntPtr propertyValues;
}

[StructLayout(LayoutKind.Sequential)]
public struct JsArrayData
{
	public int arrayLength;
	public IntPtr itemTypes;
	public IntPtr itemValues;
}

public enum JsPropertyType
{
	Function = 1,
	Buffer = 2,
	Array = 3,
	Date = 4,
	Object = 5,
	String = 6,
	Boolean = 7,
	Int32 = 8,
	UInt32 = 9,
	Number = 10,
	Null = 11
}

[SecurityCritical]
public class CoreCLREmbedding
{
	private static bool DebugMode = false;
	private static long MinDateTimeTicks = 621355968000000000;

	public delegate void CallFunctionDelegate(object payload);

    [SecurityCritical]
	public static IntPtr GetFunc(string assemblyFile, string typeName, string methodName)
    {
		Assembly assembly = Assembly.LoadFile(assemblyFile);
		DebugMessage("Assembly {0} loaded successfully", assemblyFile);

		ClrFuncReflectionWrap wrapper = ClrFuncReflectionWrap.Create(assembly, typeName, methodName);
		DebugMessage("Method {0}.{1}() loaded successfully", typeName, methodName);

		CallFunctionDelegate functionDelegate = new CallFunctionDelegate(wrapper.SimpleCall);

		return Marshal.GetFunctionPointerForDelegate(functionDelegate);
    }

	[SecurityCritical]
	public static void CallFunc(IntPtr function, IntPtr payload, int payloadType)
	{
		CallFunctionDelegate functionDelegate = Marshal.GetDelegateForFunctionPointer<CallFunctionDelegate>(function);
		functionDelegate(PayloadToObject(payload, payloadType));
	}

	private static object PayloadToObject(IntPtr payload, int payloadType)
	{
		switch ((JsPropertyType) payloadType) 
		{
			case JsPropertyType.String:
				return Marshal.PtrToStringAnsi(payload);

			case JsPropertyType.Object:
				return JsObjectToExpando(Marshal.PtrToStructure<JsObjectData>(payload));

			case JsPropertyType.Boolean:
				return Marshal.ReadByte(payload) != 0;

			case JsPropertyType.Number:
				double[] value = new double[1];
				Marshal.Copy(payload, value, 0, 1);

				return value[0];

			case JsPropertyType.Date:
				double[] ticks = new double[1];
				Marshal.Copy(payload, ticks, 0, 1);

				return new DateTime(Convert.ToInt64(ticks[0]) * 10000 + MinDateTimeTicks, DateTimeKind.Utc);

			case JsPropertyType.Null:
				return null;

			case JsPropertyType.Int32:
				return Marshal.ReadInt32(payload);

			case JsPropertyType.UInt32:
				return (uint) Marshal.ReadInt32(payload);

			case JsPropertyType.Array:
				JsArrayData arrayData = Marshal.PtrToStructure<JsArrayData>(payload);
				int[] itemTypes = new int[arrayData.arrayLength];
				IntPtr[] itemValues = new IntPtr[arrayData.arrayLength];
				List<object> array = new List<object>();

				Marshal.Copy(arrayData.itemTypes, itemTypes, 0, arrayData.arrayLength);
				Marshal.Copy(arrayData.itemValues, itemValues, 0, arrayData.arrayLength);

				for (int i = 0; i < arrayData.arrayLength; i++)
				{
					array.Add(PayloadToObject(itemValues[i], itemTypes[i]));
				}

				return array.ToArray();

			default:
				throw new Exception("Unsupported payload type: " + payloadType + ".");
		}
	}

	private static ExpandoObject JsObjectToExpando(JsObjectData payload)
	{
		ExpandoObject expando = new ExpandoObject();
		IDictionary<string, object> expandoDictionary = (IDictionary<string, object>) expando;
		int[] propertyTypes = new int[payload.propertiesCount];
		IntPtr[] propertyNamePointers = new IntPtr[payload.propertiesCount];
		IntPtr[] propertyValuePointers = new IntPtr[payload.propertiesCount];

		Marshal.Copy(payload.propertyTypes, propertyTypes, 0, payload.propertiesCount);
		Marshal.Copy(payload.propertyNames, propertyNamePointers, 0, payload.propertiesCount);
		Marshal.Copy(payload.propertyValues, propertyValuePointers, 0, payload.propertiesCount);

		for (int i = 0; i < payload.propertiesCount; i++) 
		{
			IntPtr propertyNamePointer = propertyNamePointers [i];
			IntPtr propertyValuePointer = propertyValuePointers [i];
			string propertyName = Marshal.PtrToStringAnsi(propertyNamePointer);

			expandoDictionary[propertyName] = PayloadToObject(propertyValuePointer, propertyTypes[i]);
		}

		return expando;
	}

	private static void DebugMessage(string message, params object[] parameters)
	{
		if (DebugMode) 
		{
			Console.WriteLine(message, parameters);
		}
	}

	public static void SetDebugMode(bool debugMode)
	{
		DebugMode = debugMode;
	}
}