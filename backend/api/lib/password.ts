// Password hashing for patient accounts.
//
// scrypt, matching the doctor side (frontend/app/lib/db.ts) so both account
// types are stored the same way. It replaces the bcryptjs this service used
// with Prisma — scrypt is built into Node, one dependency fewer to install
// during the 20-minute CI build.
//
// Note: any account created before the Supabase migration has a bcrypt hash and
// will not verify here. The Prisma database is not carried over, so there are
// none to migrate.

import { randomBytes, scryptSync, timingSafeEqual } from "crypto";

export function hashPassword(
  password: string,
  salt: string = randomBytes(16).toString("hex")
): { salt: string; hash: string } {
  return { salt, hash: scryptSync(password, salt, 64).toString("hex") };
}

export function verifyPassword(
  password: string,
  salt: string,
  expectedHash: string
): boolean {
  const actual = scryptSync(password, salt, 64);
  const expected = Buffer.from(expectedHash, "hex");
  // timingSafeEqual throws on a length mismatch, so check that first.
  return actual.length === expected.length && timingSafeEqual(actual, expected);
}
