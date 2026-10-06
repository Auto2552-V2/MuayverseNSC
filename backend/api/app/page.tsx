// Landing page for the API service.
//
// This container is mounted at /api/ by the event's reverse proxy (which strips
// the /api prefix before forwarding), so this page is what a browser sees at
// https://teamNN.aiforthai.in.th/api/ — it exists to make that URL informative
// instead of a 404. The game itself never loads it.

const ENDPOINTS = [
  ["POST", "/api/auth/register", "สมัครสมาชิกจากในเกม"],
  ["POST", "/api/auth/login", "เข้าสู่ระบบ"],
  ["GET", "/api/users/:id", "ดึงข้อมูล profile"],
  ["PUT", "/api/users/:id", "บันทึก profile ที่แก้จากในเกม"],
];

export default function Home() {
  return (
    <div className="mx-auto max-w-2xl px-4 py-12">
      <h1 className="text-3xl font-bold text-slate-900">Muayverse API</h1>
      <p className="mt-2 text-slate-600">
        บริการเบื้องหลังของเกม MyRehab — ไม่มีหน้าเว็บให้ใช้งานที่นี่
      </p>

      <ul className="mt-8 space-y-3">
        {ENDPOINTS.map(([method, path, note]) => (
          <li key={`${method} ${path}`} className="text-sm">
            <span className="inline-block w-14 font-mono font-semibold text-sky-600">
              {method}
            </span>
            <span className="font-mono text-slate-900">{path}</span>
            <span className="ml-3 text-slate-500">{note}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
