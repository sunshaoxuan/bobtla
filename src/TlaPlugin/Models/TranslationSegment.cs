namespace TlaPlugin.Models;

/// <summary>
/// 長文翻訳を分割した際のセグメントを表す。
/// </summary>
public readonly record struct TranslationSegment(int Index, string Text);
