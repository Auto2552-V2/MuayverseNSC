This is a [Next.js](https://nextjs.org) project bootstrapped with [`create-next-app`](https://nextjs.org/docs/app/api-reference/cli/create-next-app).

## แอพรายรับรายจ่าย (Expense Tracker)

หน้า `/expenses` ใช้ PostgreSQL + Prisma 7 (driver adapter `@prisma/adapter-pg`).

### Setup
1. แก้ `.env` ให้ `DATABASE_URL` ชี้ไปที่ Postgres ในเครื่อง:
   ```
   DATABASE_URL="postgresql://<user>:<password>@localhost:5432/<database>?schema=public"
   ```
2. สร้าง database ใน Postgres ก่อน (ถ้ายังไม่มี): `CREATE DATABASE expense_db;`
3. รัน migration ครั้งแรก:
   ```bash
   npx prisma migrate dev --name init
   ```
4. เริ่ม dev server:
   ```bash
   npm run dev
   ```
5. เปิด <http://localhost:3000/expenses>

### คำสั่งที่มีประโยชน์
- `npx prisma studio` — เปิด UI ดู/แก้ data
- `npx prisma generate` — สร้าง Prisma Client หลังแก้ schema
- `npx prisma migrate dev` — สร้าง migration ใหม่หลังแก้ schema

## Getting Started

First, run the development server:

```bash
npm run dev
# or
yarn dev
# or
pnpm dev
# or
bun dev
```

Open [http://localhost:3000](http://localhost:3000) with your browser to see the result.

You can start editing the page by modifying `app/page.tsx`. The page auto-updates as you edit the file.

This project uses [`next/font`](https://nextjs.org/docs/app/building-your-application/optimizing/fonts) to automatically optimize and load [Geist](https://vercel.com/font), a new font family for Vercel.

## Learn More

To learn more about Next.js, take a look at the following resources:

- [Next.js Documentation](https://nextjs.org/docs) - learn about Next.js features and API.
- [Learn Next.js](https://nextjs.org/learn) - an interactive Next.js tutorial.

You can check out [the Next.js GitHub repository](https://github.com/vercel/next.js) - your feedback and contributions are welcome!

## Deploy on Vercel

The easiest way to deploy your Next.js app is to use the [Vercel Platform](https://vercel.com/new?utm_medium=default-template&filter=next.js&utm_source=create-next-app&utm_campaign=create-next-app-readme) from the creators of Next.js.

Check out our [Next.js deployment documentation](https://nextjs.org/docs/app/building-your-application/deploying) for more details.
