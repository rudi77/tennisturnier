/** RFC 7807, wie das Backend es über `AddProblemDetails()` ausliefert. */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  [key: string]: unknown
}
