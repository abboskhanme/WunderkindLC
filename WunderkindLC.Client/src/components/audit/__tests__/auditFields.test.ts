import { describe, expect, it } from 'vitest'
import { parseSnapshot, snapshotFields } from '../auditFields'

describe('audit snapshots — server PascalCase keys', () => {
  it('parseSnapshot lower-cases the first letter of each top-level key', () => {
    expect(parseSnapshot('{"FullName":"Ali","Amount":5}')).toEqual({ fullName: 'Ali', amount: 5 })
  })

  it('snapshotFields renders the old → new change of a labelled field', () => {
    const fields = snapshotFields('{"Amount":100000,"Date":"2026-10-01"}', '{"Amount":150000,"Date":"2026-10-01"}')
    const amount = fields.find((f) => f.key === 'amount')
    expect(amount?.changed).toBe(true)
    expect(amount?.before).not.toBe(amount?.after)
    expect(fields.find((f) => f.key === 'date')?.changed).toBe(false)
  })

  it('ignores invalid or non-object JSON', () => {
    expect(parseSnapshot('not json')).toBeNull()
    expect(parseSnapshot('[1,2]')).toBeNull()
    expect(parseSnapshot(undefined)).toBeNull()
  })
})
