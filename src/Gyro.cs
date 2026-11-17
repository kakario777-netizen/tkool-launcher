using System;
using System.Runtime.InteropServices;

namespace TkoolLauncher
{
    /// <summary>
    /// 자이로(각속도) 읽기: 윈도우 센서 API(COM)로 기기 내장 자이로를 읽음.
    /// 방식은 Handheld Companion의 WindowsSensorManager와 같음 (vtable 슬롯 번호 직접 호출).
    /// 값 단위: 초당 각도(deg/s)
    /// </summary>
    sealed class Gyro : IDisposable
    {
        static readonly Guid GyrometerType = new Guid("09485F5A-759E-42C2-BD4B-A349B75C8643");
        static readonly Guid SensorCategoryAll = new Guid("C317C286-C468-4288-9975-D4C4587C442C");
        static readonly Guid MotionFormat = new Guid("3F8A69A2-07C5-4E48-A965-CD797AAB56D5");

        [StructLayout(LayoutKind.Sequential)]
        struct PROPERTYKEY { public Guid Fmt; public uint Pid; public PROPERTYKEY(Guid f, uint p) { Fmt = f; Pid = p; } }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        struct PROPVARIANT
        {
            [FieldOffset(0)] public ushort Vt;
            [FieldOffset(8)] public int I4;
            [FieldOffset(8)] public uint U4;
            [FieldOffset(8)] public float R4;
            [FieldOffset(8)] public double R8;
            public double Value => Vt == 3 ? I4 : Vt == 19 ? U4 : Vt == 4 ? R4 : Vt == 5 ? R8 : 0;
        }

        [ComImport, Guid("BD77DB67-45A8-42DC-8D00-6DCF15F8377A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ISensorManager
        {
            [PreserveSig] int GetSensorsByCategory(ref Guid cat, out IntPtr sensors);
            [PreserveSig] int GetSensorsByType(ref Guid type, out IntPtr sensors);
        }

        [ComImport, Guid("77A1C827-FCD2-4689-8915-9D613CC5FA3E")] class SensorManagerCo { }

        delegate int GetCountD(IntPtr self, out uint n);
        delegate int GetAtD(IntPtr self, uint i, out IntPtr s);
        delegate int GetNameD(IntPtr self, [MarshalAs(UnmanagedType.BStr)] out string name);
        delegate int GetDataD(IntPtr self, out IntPtr report);
        delegate int GetValueD(IntPtr self, ref PROPERTYKEY key, out PROPVARIANT v);

        static T Fn<T>(IntPtr obj, int slot) where T : class
        {
            IntPtr vt = Marshal.ReadIntPtr(obj);
            return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, slot * IntPtr.Size), typeof(T)) as T;
        }

        IntPtr sensor;
        public string Name = "";

        Gyro(IntPtr s, string n) { sensor = s; Name = n; }

        /// <summary>자이로를 찾으면 돌려줌, 없으면 null. 반드시 같은 스레드에서 Read 할 것</summary>
        public static Gyro Open()
        {
            IntPtr col = IntPtr.Zero;
            object mgrObj = null;
            try
            {
                mgrObj = new SensorManagerCo();
                var mgr = (ISensorManager)mgrObj;
                var t = GyrometerType;
                if (mgr.GetSensorsByType(ref t, out col) < 0 || col == IntPtr.Zero)
                {
                    var c = SensorCategoryAll;
                    if (mgr.GetSensorsByCategory(ref c, out col) < 0 || col == IntPtr.Zero) return null;
                }
                Fn<GetCountD>(col, 4)(col, out uint n);
                for (uint i = 0; i < n; i++)
                {
                    if (Fn<GetAtD>(col, 3)(col, i, out IntPtr s) < 0 || s == IntPtr.Zero) continue;
                    string name = "";
                    try { Fn<GetNameD>(s, 6)(s, out name); } catch { }
                    string ln = (name ?? "").ToLowerInvariant();
                    if (ln.Contains("gyro") || n == 1)
                        return new Gyro(s, name);
                    Marshal.Release(s);
                }
            }
            catch { }
            finally
            {
                if (col != IntPtr.Zero) Marshal.Release(col);
                if (mgrObj != null && Marshal.IsComObject(mgrObj)) Marshal.ReleaseComObject(mgrObj);
            }
            return null;
        }

        public bool Read(out double x, out double y, out double z)
        {
            x = y = z = 0;
            if (sensor == IntPtr.Zero) return false;
            IntPtr rep = IntPtr.Zero;
            try
            {
                if (Fn<GetDataD>(sensor, 13)(sensor, out rep) < 0 || rep == IntPtr.Zero) return false;
                var get = Fn<GetValueD>(rep, 4);
                var kx = new PROPERTYKEY(MotionFormat, 10);
                var ky = new PROPERTYKEY(MotionFormat, 11);
                var kz = new PROPERTYKEY(MotionFormat, 12);
                if (get(rep, ref kx, out PROPVARIANT vx) < 0) return false;
                get(rep, ref ky, out PROPVARIANT vy);
                get(rep, ref kz, out PROPVARIANT vz);
                x = vx.Value; y = vy.Value; z = vz.Value;
                return true;
            }
            catch { return false; }
            finally { if (rep != IntPtr.Zero) Marshal.Release(rep); }
        }

        public void Dispose()
        {
            if (sensor != IntPtr.Zero) { Marshal.Release(sensor); sensor = IntPtr.Zero; }
        }
    }
}
