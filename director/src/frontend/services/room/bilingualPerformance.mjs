// Stable meaning ids survive translation; changed text never inherits offsets
// from another language. The LLM supplies bindings, this module verifies them.
export function needsSemanticTranslation(plan) {
  return !!plan?.semanticUnits?.some(unit => unit.gesture);
}
export function translationBindingsPrompt(plan) {
  if (!needsSemanticTranslation(plan)) return '';
  return `Return JSON only: {"text":"the final Japanese spoken line","bindings":[{"id":"existing semantic id","spokenText":"exact Japanese phrase in text"}]}. Preserve each meaning, negation and speaker intent. Bind by meaning even if Japanese word order changes. Omit a binding if its meaning is omitted. Do not invent gestures or ids. Semantic meanings (data): ${JSON.stringify(plan.semanticUnits.map(({ id, intent, sourceText, spokenText }) => ({ id, intent, sourceText, spokenText })))}`;
}
export function rebindPerformance(plan, text, bindings = []) {
  if (!plan || plan.spokenText === text) return plan;
  const mapped = new Map(), seen = new Set();
  for (const binding of Array.isArray(bindings) ? bindings : []) {
    if (!binding || typeof binding.spokenText !== 'string' || !binding.spokenText) continue;
    if (seen.has(binding.id)) { mapped.delete(binding.id); continue; }
    seen.add(binding.id);
    const anchor = binding.spokenText, start = text.indexOf(anchor);
    if (start < 0 || text.indexOf(anchor, start + 1) >= 0 || mapped.has(binding.id)) continue;
    mapped.set(binding.id, { anchorText: anchor, start, end: start + anchor.length });
  }
  return { ...plan, spokenText: text,
    semanticUnits: (plan.semanticUnits || []).map(unit => ({ ...unit, spokenText: mapped.get(unit.id)?.anchorText || '', start: mapped.get(unit.id)?.start ?? -1, end: mapped.get(unit.id)?.end ?? -1 })),
    gestures: (plan.gestures || []).map(gesture => ({ ...gesture, anchored: true,
      ...(mapped.get(gesture.semanticId) || { anchorText: '', start: -1, end: -1 }) })) };
}
export function parseTranslatedPerformance(reply, plan, clean) {
  try {
    const data = JSON.parse(String(reply).trim().replace(/^```(?:json)?\s*|\s*```$/g, ''));
    if (typeof data.text !== 'string') return null;
    const text = clean(data.text);
    if (!text) return null;
    return { text, performance: rebindPerformance(plan, text, data.bindings) };
  } catch {
    // Some existing providers ignore JSON formatting. Preserve a plain Japanese
    // spoken line, but discard all old cross-language anchors.
    const raw = String(reply).trim();
    if (/^[{\[]|```/.test(raw) || !/[ぁ-ゖァ-ヺ]/u.test(raw)) return null;
    const text = clean(raw);
    return text ? { text, performance: rebindPerformance(plan, text) } : null;
  }
}
