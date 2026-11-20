/**
 * Narrowing helpers for JSON that arrived over the wire.
 *
 * The API is a separate deployment with its own release cadence, so its bodies are treated
 * as `unknown` and narrowed here rather than asserted into a shape. Every field a caller
 * reads passes through one of these, which is why a renamed server field shows up as a
 * missing value instead of `undefined[0]` at render time.
 */

/** True for a plain JSON object: not null, not an array. */
export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/** Reads a string field, or undefined when it is absent or holds another type. */
export function readString(record: Record<string, unknown>, key: string): string | undefined {
  const value = record[key]
  return typeof value === 'string' ? value : undefined
}
