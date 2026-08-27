using System;
using System.Collections.Generic;

namespace HeatingCameraSystem.Core.Models
{
    // ── agent-mgr.inventory.{PCId} ─────────────────────────────────────────────

    /// <summary>
    /// Manager가 보고하는 카메라 PC의 카메라 한 대. <see cref="HardwareId"/>가 안정 식별자이고
    /// <see cref="OpenCvIndex"/>는 재연결로 바뀔 수 있다.
    /// </summary>
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

    /// <summary>Manager → 서버 카메라 인벤토리 보고. 해당 PC의 카메라 목록 전체를 담는다.</summary>
    public class CameraInventoryMessage
    {
        public string                    PCId      { get; set; } = string.Empty;
        public List<CameraInventoryItem> Cameras   { get; set; } = new();
        public DateTime                  Timestamp { get; set; }
    }

    // ── server.cmd.mgr.{PCId} ──────────────────────────────────────────────────

    /// <summary>서버가 Manager에 지시할 수 있는 동작.</summary>
    public enum ManagerCommandOp
    {
        Approve,
        Reject,
        Rename,
        SetSerial,
        Restart,
        Disable
    }

    /// <summary>
    /// 서버 → Manager 명령. 대상 카메라는 <see cref="HardwareId"/>로 지정한다
    /// (재연결로 바뀌는 인덱스가 아니라 안정 식별자를 쓴다).
    /// </summary>
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

    /// <summary>로그 알림으로 승격되는 심각도. 이 등급 이상만 서버로 밀어 올린다.</summary>
    public enum LogAlertLevel { Warning, Error, Fatal }

    /// <summary>Manager → 서버 로그 알림. 로그 전문이 아니라 한 건의 요약만 담는다.</summary>
    public class LogAlertMessage
    {
        public string        PCId      { get; set; } = string.Empty;
        public string        AgentId   { get; set; } = string.Empty;
        public LogAlertLevel Level     { get; set; }
        public string        Message   { get; set; } = string.Empty;
        public DateTime      Timestamp { get; set; }
    }

    // ── server.req.log.{PCId} ──────────────────────────────────────────────────

    /// <summary>서버 → Manager 로그 덤프 요청. 응답 크기는 <see cref="MaxBytes"/>로 제한한다.</summary>
    public class LogDumpRequestMessage
    {
        public string PCId     { get; set; } = string.Empty;
        public string AgentId  { get; set; } = string.Empty;

        /// <summary>덤프 상한. 기본 5 MB.</summary>
        public int    MaxBytes { get; set; } = 5 * 1024 * 1024;
    }

    // ── agent-mgr.log.dump.{PCId} ──────────────────────────────────────────────

    /// <summary>
    /// Manager → 서버 로그 덤프 응답. 본문은 gzip 압축해 보내며,
    /// <see cref="LogDumpRequestMessage.MaxBytes"/> 상한에 걸려 잘렸으면 <see cref="IsTruncated"/>가 참이 된다.
    /// </summary>
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
