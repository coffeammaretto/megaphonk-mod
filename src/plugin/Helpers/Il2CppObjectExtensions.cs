using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using System;

namespace Megaphonk.Helpers
{
    /// <summary>
    /// The plugin compiles against Unity's managed assemblies, where UnityEngine.Object exposes neither the interop
    /// Pointer nor m_CachedPtr. At runtime every Unity object is an Il2CppObjectBase: these read both without
    /// going through il2cpp_runtime_invoke (Unity's == and every Unity property do).
    /// </summary>
    public static class Il2CppObjectExtensions
    {
        private const int OFFSET_UNRESOLVED = -1;
        private const int OFFSET_UNAVAILABLE = -2;
        private static int cachedPtrOffset = OFFSET_UNRESOLVED;

        /// <summary>
        /// Resolved once. Never throws (used inside global detours, an exception reaching native code can crash the game):
        /// if the field can't be found it is logged once and CachedPtr falls back to Unity's == null
        /// </summary>
        private static void ResolveCachedPtrOffset()
        {
            try
            {
                var objectClass = IL2CPP.GetIl2CppClass("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
                var field = objectClass == IntPtr.Zero ? IntPtr.Zero : IL2CPP.GetIl2CppField(objectClass, "m_CachedPtr");
                if (field == IntPtr.Zero)
                {
                    cachedPtrOffset = OFFSET_UNAVAILABLE;
                    Plugin.Log.LogWarning("UnityEngine.Object.m_CachedPtr not found in il2cpp metadata: falling back to Unity's == null checks");
                    return;
                }

                cachedPtrOffset = (int)IL2CPP.il2cpp_field_get_offset(field);
            }
            catch (Exception ex)
            {
                cachedPtrOffset = OFFSET_UNAVAILABLE;
                Plugin.Log.LogWarning($"Could not resolve UnityEngine.Object.m_CachedPtr, falling back to Unity's == null checks: {ex}");
            }
        }

        /// <summary>
        /// Native il2cpp object pointer (stable for the object's lifetime, pooled objects keep it)
        /// </summary>
        public static IntPtr Ptr(this UnityEngine.Object obj)
        {
            return ((Il2CppObjectBase)(object)obj).Pointer;
        }

        /// <summary>
        /// Unity's m_CachedPtr: the native engine object, zero once destroyed.
        /// If the field is unavailable: zero when Unity's == null says so, a non zero placeholder otherwise.
        /// </summary>
        public static unsafe IntPtr CachedPtr(this UnityEngine.Object obj)
        {
            if (cachedPtrOffset == OFFSET_UNRESOLVED)
            {
                ResolveCachedPtrOffset();
            }

            if (cachedPtrOffset == OFFSET_UNAVAILABLE)
            {
                return obj == null ? IntPtr.Zero : new IntPtr(1);
            }

            return *(IntPtr*)(obj.Ptr() + cachedPtrOffset);
        }

        /// <summary>
        /// m_CachedPtr read from a raw il2cpp object pointer (native detours get no wrapper). False if the field offset is
        /// unknown. Never throws.
        /// </summary>
        public static unsafe bool TryGetCachedPtr(IntPtr objectPointer, out IntPtr cachedPtr)
        {
            if (cachedPtrOffset == OFFSET_UNRESOLVED)
            {
                ResolveCachedPtrOffset();
            }

            if (cachedPtrOffset == OFFSET_UNAVAILABLE || objectPointer == IntPtr.Zero)
            {
                cachedPtr = IntPtr.Zero;
                return cachedPtrOffset != OFFSET_UNAVAILABLE;
            }

            cachedPtr = *(IntPtr*)(objectPointer + cachedPtrOffset);
            return true;
        }

        /// <summary>
        /// Same result as Unity's <c>obj != null</c> for objects created by the engine, without the native call
        /// </summary>
        public static bool IsAlive(this UnityEngine.Object obj)
        {
            return obj is not null && obj.CachedPtr() != IntPtr.Zero;
        }
    }
}
