# API ของเกม — Next.js standalone, ฟัง 3000 ใน container
#
# คู่มือยกตัวอย่าง api ที่ 8000 แต่อนุญาตให้ใช้พอร์ตอื่นได้ ("แก้ฝั่งขวาของ ports")
# ที่นี่ใช้ 3000 เท่ากับ frontend จะได้ไม่ต้องจำสองเลข — ฝั่ง host ยัง map เป็น
# ${BASE_1} ตามกติกาเหมือนเดิม

FROM node:22-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci

FROM node:22-alpine AS builder
WORKDIR /app
ENV NEXT_TELEMETRY_DISABLED=1
COPY --from=deps /app/node_modules ./node_modules
COPY . .
RUN npm run build

FROM node:22-alpine AS runner
WORKDIR /app

ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    # ต้องเป็น 0.0.0.0 — 127.0.0.1 ใน container แปลว่า Docker เข้าไม่ถึง
    HOSTNAME=0.0.0.0

RUN addgroup -S -g 1001 nodejs && adduser -S -u 1001 -G nodejs nextjs

COPY --from=builder --chown=nextjs:nodejs /app/.next/standalone ./
COPY --from=builder --chown=nextjs:nodejs /app/.next/static ./.next/static
COPY --from=builder --chown=nextjs:nodejs /app/public ./public

USER nextjs
EXPOSE 3000
CMD ["node", "server.js"]
