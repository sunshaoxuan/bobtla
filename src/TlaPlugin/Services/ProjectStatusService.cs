using System;
using System.Collections.Generic;
using System.Linq;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 開発進捗を提供し前端計画を加速するためのサービス。
/// </summary>
public class ProjectStatusService
{
    private static readonly IReadOnlyList<StageStatus> Stages = new List<StageStatus>
    {
        new("stage0", "阶段 0：需求吸收", "需求说明书の読解と技術選定を完了。", true),
        new("stage1", "阶段 1：服务编排", "多模型路由・预算守卫・术语合并を実装。", true),
        new("stage2", "阶段 2：Teams 适配", "消息扩展と Adaptive Card 応答を提供。", true),
        new("stage3", "阶段 3：持久化与测试", "SQLite 草稿保存と xUnit カバレッジを確立。", true),
        new("stage4", "阶段 4：合规加固", "区域・认证・禁译语チェックを導入。", true),
        new("stage5", "阶段 5：性能护栏", "翻訳キャッシュと速率制御を導入。", true),
        new("stage6", "阶段 6：密钥与 OBO", "Key Vault 秘密キャッシュと OBO 代理を完了。", true),
        new("stage7", "阶段 7：多语广播", "追加言語の一括翻訳と卡片呈现を完了。", true),
        new("stage8", "阶段 8：多模型互联", "ConfigurableChatModelProvider で外部 API を統合。", true),
        new("stage9", "阶段 9：前端体验", "Teams Tab・设置画面・联调テストを実装。", false)
    };

    private static readonly IReadOnlyList<string> NextSteps = new List<string>
    {
        "完成真实模型联通与延迟监控",
        "替换 Key Vault/OBO 模拟为生产 SDK",
        "实现 Teams Tab 设置页与术语管理前端",
        "启动前后端联调与端到端测试"
    };

    /// <summary>
    /// 現在の進捗スナップショットを返却する。
    /// </summary>
    public ProjectStatusSnapshot GetSnapshot()
    {
        var current = Stages.Last(s => s.Completed);
        var overallPercent = CalculateOverallPercent();
        var frontend = new FrontendProgress(
            CompletionPercent: 0,
            DataPlaneReady: true,
            UiImplemented: false,
            IntegrationReady: false);

        return new ProjectStatusSnapshot(current.Id, Stages, NextSteps, overallPercent, frontend);
    }

    private static int CalculateOverallPercent()
    {
        var completed = Stages.Count(stage => stage.Completed);
        var percent = (double)completed / Stages.Count * 100;
        return (int)Math.Round(percent, MidpointRounding.AwayFromZero);
    }
}
