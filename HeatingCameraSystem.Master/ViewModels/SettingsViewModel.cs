using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly HashSet<string> _subscribedAgents = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<SerialConfigAckMessage>> _pendingAcks = new();

        public ObservableCollection<CameraSerialSettings> CameraSettings { get; } = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveAndSendCommand))]
        private CameraSerialSettings? _selectedSettings;

        [ObservableProperty]
        private string _statusMessage = "대기중";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveAndSendCommand))]
        private bool _isSending;

        public SettingsViewModel()
        {
            LoadSettings();
        }

        private void LoadSettings()
        {
            var saved = AppServices.CameraSerialSettingsRepo
                .GetAllAsync().GetAwaiter().GetResult()
                .OrderBy(s => s.CameraIndex);

            foreach (var s in saved)
                CameraSettings.Add(s);
        }

        [RelayCommand]
        private void AddCamera()
        {
            int nextIndex = CameraSettings.Count > 0
                ? CameraSettings.Max(s => s.CameraIndex) + 1
                : 0;

            var entry = new CameraSerialSettings { CameraIndex = nextIndex };
            CameraSettings.Add(entry);
            SelectedSettings = entry;
        }

        [RelayCommand(CanExecute = nameof(CanSend))]
        private async Task SaveAndSendAsync()
        {
            IsSending = true;
            StatusMessage = "전송 중...";

            await AppServices.CameraSerialSettingsRepo.UpsertAsync(SelectedSettings!);
            await AppServices.ApplySerialSettingsLocallyAsync(SelectedSettings!);

            if (AppServices.NatsService == null)
            {
                StatusMessage = "✔ 저장 완료 (NATS 미연결)";
                IsSending = false;
                return;
            }

            string agentId = $"Agent_{SelectedSettings!.CameraIndex}";

            if (_subscribedAgents.Add(agentId))
            {
                await AppServices.NatsService.SubscribeSerialConfigAckAsync(agentId, ack =>
                {
                    if (_pendingAcks.TryRemove(ack.AgentId, out var tcs))
                        tcs.TrySetResult(ack);
                });
            }

            var ackTcs = new TaskCompletionSource<SerialConfigAckMessage>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingAcks[agentId] = ackTcs;

            await AppServices.NatsService.PublishSerialConfigAsync(new SerialConfigMessage
            {
                AgentId   = agentId,
                Settings  = SelectedSettings,
                Timestamp = DateTime.UtcNow
            });

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var ack = await ackTcs.Task.WaitAsync(cts.Token);
                StatusMessage = ack.IsSuccess
                    ? $"✔ 적용 완료 ({agentId})"
                    : $"✘ 오류: {ack.ErrorMessage}";
            }
            catch (OperationCanceledException)
            {
                _pendingAcks.TryRemove(agentId, out _);
                StatusMessage = "✘ 응답 없음 (5초 초과)";
            }

            IsSending = false;
        }

        private bool CanSend() => SelectedSettings != null && !IsSending;
    }
}
