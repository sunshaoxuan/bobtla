const ACTIVE_STAGE_FALLBACK_PROGRESS = 50;

function normalizeStages(stages) {
  if (!Array.isArray(stages)) {
    return [];
  }
  return stages
    .filter((stage) => stage && typeof stage === "object")
    .map((stage) => ({
      id: stage.id ?? stage.Id ?? "",
      name: stage.name ?? stage.Name ?? "",
      objective: stage.objective ?? stage.Objective ?? "",
      completed: Boolean(stage.completed ?? stage.Completed ?? false),
      deliverables: stage.deliverables ?? stage.Deliverables ?? [],
      testFocuses: stage.testFocuses ?? stage.TestFocuses ?? []
    }));
}

export function groupStagesForTimeline(stages, activeStageId) {
  const normalized = normalizeStages(stages);
  return normalized.map((stage, index) => {
    const isActive = stage.id === activeStageId || (!activeStageId && !stage.completed && normalized.slice(0, index).every((item) => item.completed));
    let progress = stage.completed ? 100 : 0;
    if (isActive && !stage.completed) {
      progress = ACTIVE_STAGE_FALLBACK_PROGRESS;
    }
    return {
      order: index + 1,
      isActive,
      progress,
      ...stage
    };
  });
}

export function buildStatusCards(status, roadmap) {
  const safeStatus = status ?? {};
  const safeRoadmap = roadmap ?? {};
  const stages = safeRoadmap.stages ?? safeStatus.stages ?? [];
  const activeStageId = safeStatus.currentStageId ?? safeRoadmap.activeStageId;
  const timeline = groupStagesForTimeline(stages, activeStageId);
  const completedCount = timeline.filter((stage) => stage.completed).length;
  const overallPercent = typeof safeStatus.overallCompletionPercent === "number"
    ? safeStatus.overallCompletionPercent
    : timeline.length === 0
      ? 0
      : Math.round((completedCount / timeline.length) * 100);
  const frontend = safeStatus.frontend ?? {};
  const activeStage = timeline.find((stage) => stage.isActive) ?? timeline.find((stage) => !stage.completed) ?? timeline[timeline.length - 1] ?? null;

  return {
    timeline,
    overallPercent,
    activeStage,
    nextSteps: Array.isArray(safeStatus.nextSteps) ? safeStatus.nextSteps : [],
    frontend: {
      completionPercent: typeof frontend.completionPercent === "number" ? frontend.completionPercent : 0,
      dataPlaneReady: Boolean(frontend.dataPlaneReady),
      uiImplemented: Boolean(frontend.uiImplemented),
      integrationReady: Boolean(frontend.integrationReady)
    },
    tests: Array.isArray(safeRoadmap.tests) ? safeRoadmap.tests : []
  };
}

export function formatLocaleOptions(locales) {
  if (!Array.isArray(locales)) {
    return [];
  }
  return locales
    .filter((locale) => locale && typeof locale === "object")
    .map((locale, index) => ({
      id: locale.id ?? locale.code ?? `locale-${index}`,
      name: locale.displayName ?? locale.name ?? locale.id ?? `Locale ${index + 1}`,
      isDefault: Boolean(locale.isDefault ?? locale.default)
    }))
    .sort((a, b) => {
      if (a.isDefault && !b.isDefault) return -1;
      if (!a.isDefault && b.isDefault) return 1;
      return a.name.localeCompare(b.name, "zh-Hans-CN");
    });
}
