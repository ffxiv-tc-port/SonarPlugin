using Dalamud.Interface.Windowing;
using ImGuiNET;
using Sonar;
using Sonar.Config;
using Sonar.Data;
using Sonar.Data.Extensions;
using Sonar.Models;
using Sonar.Utilities;
using SonarPlugin.Config;
using SonarPlugin.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using DryIocAttributes;

namespace SonarPlugin.GUI
{
    [ExportMany]
    [SingletonReuse]
    public sealed class LodestoneVerifyWindow : Window, IHostedService
    {
        private LodestoneVerificationNeeded? _need;
        private LodestoneVerificationResult? _result;
        private double _requestTimestamp;

        private SonarMeta Meta => this.Client.Meta;
        private SonarConfig Config { get; }
        private SonarConfiguration Configuration { get; }
        private SonarClient Client { get; }
        private WindowSystem Windows { get; }

        [SuppressMessage("Critical Code Smell", "S3265", Justification = "Its a flag!")]
        public LodestoneVerifyWindow(SonarConfig config, SonarPlugin plugin, SonarClient client, WindowSystem windows) : base("Sonar Lodestone Verification")
        {
            this.Config = config;
            this.Configuration = plugin.Configuration;
            this.Client = client;
            this.Windows = windows;

            this.Flags |= ImGuiWindowFlags.NoSavedSettings;

            this.Size = new(320, 480);
            this.SizeCondition |= ImGuiCond.Appearing;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            this.Windows.AddWindow(this);
            this.Meta.VerificationNeeded += this.NeededHandler;
            this.Meta.VerificationResult += this.ResultHandler;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            this.Windows.RemoveWindow(this);
            this.Meta.VerificationNeeded -= this.NeededHandler;
            this.Meta.VerificationResult -= this.ResultHandler;
            return Task.CompletedTask;
        }

        public override void PreOpenCheck()
        {
            if (this.ShouldSuppress()) this.OnClose();
        }

        private bool ShouldSuppress()
        {
            if (this._need is null || !this.Config.Contribute.Global) return true; // Nothing to show and no need to nag user
            if (this.Configuration.SuppressVerification is SuppressVerification.Always) return true;
            if (this.Configuration.SuppressVerification is SuppressVerification.UnlessRequired) return !this._need.Required;
            return false;
        }

        public override void Draw()
        {
            ImGui.TextWrapped("你目前登入的角色需要進行拉諾西亞漫遊指南 (Lodestone) 驗證:");
            ImGui.Indent(); this.DrawPlayerInfo(); ImGui.Unindent();

            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            this.DrawReason();

            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            this.DrawVerification();

            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            ImGui.TextWrapped("你可以在 /sonarconfig 的「一般」分頁、Lodestone 設定中關閉此對話框。");
        }

        private void DrawPlayerInfo()
        {
            Debug.Assert(this._need is not null);

            ImGui.TextUnformatted($"{this.Meta.PlayerInfo}");
            if (this._need.LodestoneId != -1)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted($" (ID: {this._need.LodestoneId})");
            }
        }

        [SuppressMessage("Minor Code Smell", "S1075", Justification = "Well-known URLs")]
        private void DrawReason()
        {
            Debug.Assert(this._need is not null);

            switch (this._need.Reason)
            {
                case LodestoneVerificationReason.Unknown:
                    {
                        ImGui.TextWrapped("未指定的原因。");
                        ImGui.Spacing();
                        ImGui.TextWrapped("請聯絡 Sonar 支援以取得協助。");
                    }
                    break;

                case LodestoneVerificationReason.NotVerified:
                    {
                        ImGui.TextWrapped("你的角色目前尚未通過驗證。");
                    }
                    break;

                case LodestoneVerificationReason.NotFound:
                    {
                        ImGui.TextWrapped("找不到你的角色 Lodestone 個人檔案。");
                        ImGui.Spacing();
                        ImGui.TextWrapped("請將你角色的 Lodestone 個人檔案設為可搜尋且公開。驗證完成後可再設回私人狀態。");
                    }
                    break;

                case LodestoneVerificationReason.PrivateProfile:
                    {
                        ImGui.TextWrapped("你的角色 Lodestone 個人檔案為私人狀態。");
                        ImGui.Spacing();
                        ImGui.TextWrapped("請將你的 Lodestone 個人檔案設為公開以便進行驗證。驗證完成後可再設回私人狀態。");
                    }
                    break;

                case LodestoneVerificationReason.Renamed:
                    {
                        ImGui.TextWrapped("你的角色已改名，且角色的 Lodestone 個人檔案為私人狀態。");
                        ImGui.Spacing();
                        ImGui.TextWrapped("請將你的 Lodestone 個人檔案設為公開以便進行驗證。驗證完成後可再設回私人狀態。");
                    }
                    break;

                case LodestoneVerificationReason.HashMismatch:
                    {
                        ImGui.TextWrapped("你的角色雜湊值不符，且角色的 Lodestone 個人檔案為私人狀態。");
                        ImGui.Spacing();
                        ImGui.TextWrapped("請將你的 Lodestone 個人檔案設為公開以便進行驗證。驗證完成後可再設回私人狀態。");
                    }
                    break;

                case LodestoneVerificationReason.Stale:
                    {
                        ImGui.TextWrapped("Sonar 儲存的角色 Lodestone 資訊已過期。");
                        ImGui.Spacing();
                        ImGui.TextWrapped("請將你的 Lodestone 個人檔案設為公開以便進行驗證。驗證完成後可再設回私人狀態。");
                    }
                    break;

                default:
                    {
                        ImGui.TextWrapped("無法提供原因。");
                        ImGui.Spacing();
                        ImGui.TextWrapped("請更新你的 Sonar 外掛，或聯絡支援以取得協助。");
                    }
                    break;
            }

            if (this._need.Code is not null)
            {
                ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                ImGui.TextWrapped("請將以下代碼加入你角色 Lodestone 個人檔案的自我介紹欄:");
                ImGui.Indent();
                ImGui.TextUnformatted(this._need.Code);
                ImGui.SameLine();
                if (ImGui.Button("複製")) ImGui.SetClipboardText(this._need.Code);
                ImGui.Unindent();
            }

            var world = this.Meta.PlayerInfo?.GetHomeWorld();
            var baseUrl = (world?.RegionId ?? 0) switch
            {
                1 => "https://jp.finalfantasyxiv.com/lodestone",
                3 => "https://eu.finalfantasyxiv.com",
                _ => "https://na.finalfantasyxiv.com",
            };


            if (this._need.LodestoneId !=-1)
            {
                if (ImGui.Button("開啟 Lodestone 個人檔案")) _ = Task.Run(() => ShellUtils.ShellExecute($"{baseUrl}/character/{this._need.LodestoneId}/"));
                ImGui.SameLine();
            }
            if (ImGui.Button("開啟 Lodestone 網站")) _ = Task.Run(() => ShellUtils.ShellExecute($"{baseUrl}"));
        }

        private void DrawVerification()
        {
            Debug.Assert(this._need is not null);

            var curTime = UnixTimeHelper.SyncedUnixNow;
            var reqTime = this._need.RequiredTime;

            if (this._need.Required) ImGui.TextWrapped("此項驗證為必要項目。");
            else if (reqTime > 0)
            {
                var remTime = reqTime - curTime;
                if (remTime < SonarConstants.EarthMinute * 1) // This is to avoid confusion of being allowed a few seconds (might still cause confusion anyway....)
                {
                    ImGui.TextWrapped("此項驗證為必要項目。");
                }
                else if (remTime > SonarConstants.EarthDay * 2)
                {
                    ImGui.TextWrapped($"此項驗證將在 {(int)(remTime / SonarConstants.EarthDay)} 天後成為必要項目。");
                }
                else if (remTime > SonarConstants.EarthHour * 1)
                {
                    ImGui.TextWrapped($"此項驗證將在 {(int)(remTime / SonarConstants.EarthHour)} 小時後成為必要項目。");
                }
                else if (remTime > SonarConstants.EarthMinute * 5)
                {
                    ImGui.TextWrapped($"此項驗證將在 {(int)(remTime / SonarConstants.EarthMinute)} 分鐘後成為必要項目。");
                }
                else
                {
                    ImGui.TextWrapped("此項驗證將在不到 5 分鐘後成為必要項目。");
                }
            }
            else
            {
                ImGui.TextWrapped("此項驗證目前並非必要項目。");
            }

            if (ImGui.Button("驗證"))
            {
                this.Meta.RequestVerification();
                this._requestTimestamp = curTime;
            }

            if (this._requestTimestamp > 0)
            {
                ImGui.SameLine();
                if (this._result is null)
                {
                    var runTime = curTime - this._requestTimestamp;
                    if (runTime > SonarConstants.EarthSecond * 30)
                    {
                        ImGui.TextWrapped("驗證逾時");
                    }
                    else
                    {
                        var dots = new string(Enumerable.Repeat('.', ((int)(runTime / SonarConstants.EarthSecond * 3) % 3) + 1).ToArray());
                        ImGui.TextWrapped($"驗證進行中{dots}");
                    }
                }
                else
                {
                    ImGui.TextWrapped("驗證失敗"); // Window wouldn't be visible if succeded
                }
            }
            ImGui.TextWrapped("若在此項目成為必要項目前尚未完成驗證，你將只能以本機模式使用 Sonar。");
        }

        private void NeededHandler(SonarMeta _, LodestoneVerificationNeeded need)
        {
            this._need = need;
            this.IsOpen = true;
        }

        private void ResultHandler(SonarMeta _, LodestoneVerificationResult result)
        {
            this._result = result;
            if (this._result.Success) this.OnClose();
        }

        public override void OnClose()
        {
            // Poof and reset all variables
            this.IsOpen = false; // This is here because its sometimes called manually
            this._need = null;
            this._result = null;
            this._requestTimestamp = 0;

            base.OnClose();
        }

        public override void OnOpen()
        {
            // Make sure its brought to front
            this.BringToFront();

            base.OnOpen();
        }
    }
}
