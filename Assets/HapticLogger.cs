// PegTransferHapticLogger3DS.cs
// Attach to an empty GameObject.
// Assign two HapticPlugin components (one per device).
// This script logs in FixedUpdate to match HapticPlugin's update cadence. :contentReference[oaicite:1]{index=1}

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Globalization;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor.SearchService;
using UnityEditor;
using UnityEngine.SceneManagement;

public sealed class PegTransferHapticLogger3DS : MonoBehaviour
{
    public enum EventCode : int
    {
        NONE = 0,
        GRAB_BEGIN = 1,
        GRAB_END = 2,
        RING_CONTACT_BEGIN = 3,
        RING_CONTACT_END = 4,
        PEG_PLACE = 5,
        PEG_DROP = 6,
        OTHER = 99
    }

    [Serializable]
    public sealed class DeviceChannel
    {
        [Tooltip("Label used in filename.")]
        public string deviceLabel = "device0";

        [Tooltip("Reference to the 3D Systems HapticPlugin for this device.")]
        public HapticPlugin plugin;

        [NonSerialized] public volatile int isHolding = 0;
        [NonSerialized] public volatile int heldRingId = -1;
        [NonSerialized] public volatile int eventCode = 0;

        [NonSerialized] public string filepath;
        [NonSerialized] public ConcurrentQueue<string> queue;
        [NonSerialized] public Thread writerThread;
        [NonSerialized] public volatile int writerRun = 0;
        [NonSerialized] public StreamWriter writer;

        [NonSerialized] public long lastSystemNs = 0;
        [NonSerialized] public float fpsSmoothed = 0f;
    }

    [Header("Devices")]
    public DeviceChannel device0 = new DeviceChannel { deviceLabel = "left_device" };

    [Header("Logging")]
    public string subfolder = "HapticLogs";
    public int flushEveryNLines = 256;

    readonly DeviceChannel[] _devices = new DeviceChannel[2];
    readonly Stopwatch _sw = new Stopwatch();
    string _sessionStamp;
    volatile int _running = 0;

    void Awake()
    {
        _devices[0] = device0;

        _sessionStamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        _sw.Start();
    }

    void OnEnable()
    {
        _running = 1;

        Directory.CreateDirectory(Path.Combine(Application.persistentDataPath, subfolder));

        for (int i = 0; i < 1; i++)
            InitDeviceWriter(_devices[i], i);
    }

    void OnDisable()
    {
        _running = 0;

        for (int i = 0; i < _devices.Length; i++)
            StopDeviceWriter(_devices[i]);
    }

    void FixedUpdate()
    {
        if (_running == 0) return;

        // Unity wall clock in seconds (monotonic during play)
        double unityRealtime = Time.realtimeSinceStartupAsDouble;

        for (int i = 0; i < 1; i++)
        {
            var d = _devices[i];
            if (d.plugin == null) continue;

            // Global monotonic reference time in ns
            long systemNs = GetSystemTimeNs();
            double dtFrame = 0.0;
            if (d.lastSystemNs != 0) dtFrame = (systemNs - d.lastSystemNs) * 1e-9;
            d.lastSystemNs = systemNs;

            if (dtFrame > 1e-6)
            {
                float fps = (float)(1.0 / dtFrame);
                d.fpsSmoothed = (d.fpsSmoothed <= 0f) ? fps : Mathf.Lerp(d.fpsSmoothed, fps, 0.05f);
            }

            // Device state comes from HapticPlugin (already updated each FixedUpdate). :contentReference[oaicite:2]{index=2}
            Vector3 posMm = d.plugin.CurrentPosition;     // expected mm per plugin comments/imports
            Vector3 forceN = d.plugin.CurrentForce;       // N :contentReference[oaicite:3]{index=3}

            // Plugin stores JointAngles/GimbalAngles in degrees (Rad2Deg conversion in UpdateDeviceInformation). :contentReference[oaicite:4]{index=4}
            Vector3 jointRad = d.plugin.JointAngles * Mathf.Deg2Rad;
            Vector3 gimbalRad = d.plugin.GimbalAngles * Mathf.Deg2Rad;

            // Orientation: use the plugin's CollisionMesh transform if assigned, else plugin transform.
            // HapticPlugin updates CollisionMesh pose in UpdateTransfrom(). :contentReference[oaicite:5]{index=5}
            Transform poseT = null;
            if (d.plugin.CollisionMesh != null) poseT = d.plugin.CollisionMesh.transform;
            else if (d.plugin.VisualizationMesh != null) poseT = d.plugin.VisualizationMesh.transform;
            else poseT = d.plugin.transform;

            Quaternion rot = poseT.rotation;

            int holding = d.isHolding;
            int ringId = d.heldRingId;
            int ev = Interlocked.Exchange(ref d.eventCode, (int)EventCode.NONE);

            string line = BuildCsvLine(
                systemNs, unityRealtime,
                posMm.x, posMm.y, posMm.z,
                rot.x, rot.y, rot.z, rot.w,
                jointRad, gimbalRad,
                forceN,
                holding, ringId, ev,
                d.fpsSmoothed,
                dtFrame
            );

            d.queue.Enqueue(line);
        }
    }

    static string BuildCsvLine(
        long systemNs, double unityRealtime,
        float posXmm, float posYmm, float posZmm,
        float qx, float qy, float qz, float qw,
        Vector3 jointRad, Vector3 gimbalRad,
        Vector3 forceN,
        int isHolding, int heldRingId, int eventCode,
        float fps, double dtFrame)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(320);

        sb.Append(systemNs.ToString(c)).Append(',');
        sb.Append(unityRealtime.ToString("R", c)).Append(',');

        sb.Append(posXmm.ToString("R", c)).Append(',');
        sb.Append(posYmm.ToString("R", c)).Append(',');
        sb.Append(posZmm.ToString("R", c)).Append(',');

        sb.Append(qx.ToString("R", c)).Append(',');
        sb.Append(qy.ToString("R", c)).Append(',');
        sb.Append(qz.ToString("R", c)).Append(',');
        sb.Append(qw.ToString("R", c)).Append(',');

        sb.Append(jointRad.x.ToString("R", c)).Append(',');
        sb.Append(jointRad.y.ToString("R", c)).Append(',');
        sb.Append(jointRad.z.ToString("R", c)).Append(',');

        sb.Append(gimbalRad.x.ToString("R", c)).Append(',');
        sb.Append(gimbalRad.y.ToString("R", c)).Append(',');
        sb.Append(gimbalRad.z.ToString("R", c)).Append(',');

        sb.Append(forceN.x.ToString("R", c)).Append(',');
        sb.Append(forceN.y.ToString("R", c)).Append(',');
        sb.Append(forceN.z.ToString("R", c)).Append(',');

        sb.Append(isHolding).Append(',');
        sb.Append(heldRingId).Append(',');
        sb.Append(eventCode).Append(',');

        sb.Append(fps.ToString("R", c)).Append(',');
        sb.Append(dtFrame.ToString("R", c));

        return sb.ToString();
    }

    long GetSystemTimeNs()
    {
        long ticks = _sw.ElapsedTicks;
        double ns = (double)ticks * (1e9 / Stopwatch.Frequency);
        return (long)ns;
    }

    void InitDeviceWriter(DeviceChannel d, int index)
    {
        d.queue = new ConcurrentQueue<string>();
        d.writerRun = 1;

        string dir = Path.Combine(Application.persistentDataPath, subfolder);
        string filename = $"{SceneManager.GetActiveScene().name}_{_sessionStamp}_{d.deviceLabel}_idx{index}.csv";
        d.filepath = Path.Combine(dir, filename);

        d.writer = new StreamWriter(d.filepath, append: false, encoding: new UTF8Encoding(false), bufferSize: 1 << 16);

        d.writer.WriteLine(
            "system_time_ns,unity_realtime_s," +
            "pos_mm_x,pos_mm_y,pos_mm_z," +
            "rot_q_x,rot_q_y,rot_q_z,rot_q_w," +
            "joint_rad_0,joint_rad_1,joint_rad_2," +
            "gimbal_rad_0,gimbal_rad_1,gimbal_rad_2," +
            "force_N_x,force_N_y,force_N_z," +
            "is_holding,held_ring_id,event_code," +
            "fps,dt_frame_s"
        );
        d.writer.Flush();

        d.writerThread = new Thread(() => WriterLoop(d))
        {
            IsBackground = true,
            Name = $"HapticLoggerWriter_{d.deviceLabel}"
        };
        d.writerThread.Start();
    }

    void StopDeviceWriter(DeviceChannel d)
    {
        if (d == null) return;

        d.writerRun = 0;

        if (d.writerThread != null && d.writerThread.IsAlive)
            d.writerThread.Join(500);

        if (d.writer != null)
        {
            int drained = 0;
            while (d.queue != null && d.queue.TryDequeue(out var line))
            {
                d.writer.WriteLine(line);
                drained++;
                if (drained % flushEveryNLines == 0) d.writer.Flush();
            }
            d.writer.Flush();
            d.writer.Dispose();
            d.writer = null;
        }

        d.queue = null;
        d.writerThread = null;
    }

    void WriterLoop(DeviceChannel d)
    {
        int linesSinceFlush = 0;

        while (d.writerRun == 1)
        {
            if (d.queue.TryDequeue(out var line))
            {
                d.writer.WriteLine(line);
                linesSinceFlush++;

                if (linesSinceFlush >= flushEveryNLines)
                {
                    d.writer.Flush();
                    linesSinceFlush = 0;
                }
            }
            else
            {
                Thread.Sleep(1);
            }
        }
    }

    // API for your grasp/peg scripts
    public void SetHolding(int deviceIndex, bool holding, int ringId)
    {
        if ((uint)deviceIndex >= 2u) return;
        var d = _devices[deviceIndex];
        d.isHolding = holding ? 1 : 0;
        d.heldRingId = holding ? ringId : -1;
    }

    public void LogEvent(int deviceIndex, EventCode code)
    {
        if ((uint)deviceIndex >= 2u) return;
        Interlocked.Exchange(ref _devices[deviceIndex].eventCode, (int)code);
    }

    public string GetDeviceFilePath(int deviceIndex)
    {
        if ((uint)deviceIndex >= 2u) return null;
        return _devices[deviceIndex].filepath;
    }
}
