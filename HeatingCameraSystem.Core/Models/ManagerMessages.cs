using System;
using System.Collections.Generic;

namespace HeatingCameraSystem.Core.Models
{
    // ── agent-mgr.inventory.{PCId} ─────────────────────────────────────────────

    public class CameraInventoryItem
    {
        public string   HardwareId  { get; set; } = string.Empty;
        public string   Alias       { get; set; } = string.Empty;
        public string   AgentId     { get; set; } = string.Empty;
        public int      OpenCvIndex { get; set; }
        public bool     IsApproved  { get; set; }
        public bool     IsRunning   { get; set; }
        public DateTime LastSeen    { get; set; }
    }

    public class CameraInventoryMessage
    {
        public string                    PCId      { get; set; } = string.Empty;
        public List<CameraInventoryItem> Cameras   { get; set; } = new();
        public DateTime                  Timestamp { get; set; }
    }

    // ── server.cmd.mgr.{PCId} ──────────────────────────────────────────────────

    public enum ManagerCommandOp
    {
        Approve,
        Reject,
        Rename,
        SetSerial,
        Restart,
        Disable
    }

    public class ManagerCommandMessage
    {
        public string           PCId        { get; set; } = string.Empty;
        public ManagerCommandOp Op          { get; set; }
        public string           HardwareId  { get; set; } = string.Empty;
        /// <summary>Op별 추가 데이터 (JSON 직렬화 그대로 전달)</summary>
        public string           Payload     { get; set; } = string.Empty;
        public DateTime         Timestamp   { get; set; }
    }

    // ── agent-mgr.log.alert.{PCId} ─────────────────────────────────────────────

    public enum LogAlertLevel { Warning, Error, Fatal }

    public class LogAlertMessage
    {
        public string        PCId      { get; set; } = string.Empty;
        public string        AgentId   { get; set; } = string.Empty;
        public LogAlertLevel Level     { get; set; }
        public string        Message   { get; set; } = string.Empty;
        public DateTime      Timestamp { get; set; }
    }

    // ── server.req.log.{PCId} ──────────────────────────────────────────────────

    public class LogDumpRequestMessage
    {
        public string PCId     { get; set; } = string.Empty;
        public string AgentId  { get; set; } = string.Empty;
        public int    MaxBytes { get; set; } = 5 * 1024 * 1024; // 5 MB default
    }

    // ── agent-mgr.log.dump.{PCId} ──────────────────────────────────────────────

    public class LogDumpMessage
    {
        public string  PCId          { get; set; } = string.Empty;
        public string  AgentId       { get; set; } = string.Empty;
        public byte[]  GzipBytes     { get; set; } = Array.Empty<byte>();
        public long    OriginalBytes { get; set; }
        public bool    IsTruncated   { get; set; }
        public DateTime Timestamp    { get; set; }
    }
}
