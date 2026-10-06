// พาเบราว์เซอร์ออกจากหน้าเกมไปหน้าอื่นในโดเมนเดียวกัน — ใช้โดย DoctorPortalLink.cs
//
// ทำไมไม่ใช้ Application.OpenURL: บน WebGL มันถูกแปลเป็น window.open() ซึ่ง
//   1. เปิดแท็บใหม่ ทั้งที่เคสนี้เราอยากออกจากเกมไปเลย
//   2. โดน popup blocker กินบ่อย เพราะ Unity ประมวลผลคลิกไม่ทันในเฟรมเดียวกับ
//      event ของเบราว์เซอร์ พอเรียก window.open ทีหลังเบราว์เซอร์เลยไม่นับว่า
//      เป็น user gesture
//
// เปลี่ยน location ของแท็บเดิมไม่มีข้อจำกัดพวกนี้
//
// url ที่รับเข้ามาเป็น path ล้วน ("/login") เบราว์เซอร์เติม origin ให้เอง
// จึงใช้ได้ทั้ง localhost และ https://teamNN.aiforthai.in.th โดยไม่ต้อง build ใหม่

mergeInto(LibraryManager.library, {
  TictaOpenSiteUrl: function (urlPtr) {
    window.location.href = UTF8ToString(urlPtr);
  },

  // เปิด/ปิดกล้องของหน้าเว็บ — เรียกจาก PoseCameraStarter.cs ตอนเข้า/ออกซีนมินิเกม
  //
  // ตัวจริงนิยามอยู่ใน Assets/WebGLTemplates/TICTA/index.html ไม่ใช่ที่นี่ เพราะมันต้อง
  // แตะ <video> กับ <div id="phone"> ซึ่งเป็นของ template ไม่ใช่ของ Unity
  // ที่นี่เป็นแค่สะพาน — เช็คก่อนเรียกเผื่อ build ไปใช้ template อื่นที่ไม่มีฟังก์ชันนี้
  // จะได้เงียบ ๆ แทนที่จะพังทั้งเกม
  TictaStartPoseCamera: function () {
    if (typeof window.TictaStartPoseCamera === "function") { window.TictaStartPoseCamera(); }
  },

  TictaStopPoseCamera: function () {
    if (typeof window.TictaStopPoseCamera === "function") { window.TictaStopPoseCamera(); }
  },

  // แจ้งผลแมตช์บนหน้าเว็บแล้วพาออกจากเกมทันที — เรียกจาก StageController.cs ตอนจบแมตช์
  //
  // ใช้เมื่อ build มาแค่ SampleScene ตัวเดียว จึงไม่มีซีนหน้า Reward ให้โหลดต่อ
  //
  // ทำไมเรียงแบบนี้ได้: alert() เป็น modal ที่บล็อกเธรดหลักของเบราว์เซอร์ (ซึ่งเป็นเธรด
  // เดียวกับที่ Unity รันอยู่) บรรทัดถัดไปจะไม่ทำงานจนผู้เล่นกด OK — ลำดับจึงเป็น
  // แจ้งผล -> กด OK -> ออกจากเกม ตามที่ต้องการเป๊ะ ๆ โดยไม่ต้องใช้ callback
  //
  // ไม่ต้องปิดกล้องเอง เพราะการเปลี่ยน location ทำให้เบราว์เซอร์คืน MediaStream ให้เองอยู่แล้ว
  //
  // url ว่าง = ถอยกลับหน้าเดิมที่พาเข้าเกมมา
  TictaAlertAndLeave: function (messagePtr, urlPtr) {
    var message = UTF8ToString(messagePtr);
    var url = UTF8ToString(urlPtr);

    if (message) { window.alert(message); }

    if (url) {
      window.location.href = url;
    } else if (window.history.length > 1) {
      window.history.back();
    } else {
      // เปิดเกมมาตรง ๆ ไม่มีหน้าก่อนหน้าให้ถอยกลับ — อย่างน้อยพาไปหน้าแรกของโดเมน
      window.location.href = "/";
    }
  },

  // ลบ query string ทิ้งจาก address bar โดยไม่โหลดหน้าใหม่ — เรียกจาก TherapyReward.cs
  // ทันทีที่อ่าน ?session=<id> ได้แล้ว
  //
  // ถ้าไม่ลบ พอผู้เล่นกด refresh หรือเปิดจากประวัติ เกมจะเด้งเข้าหน้ารางวัลของ
  // เซสชันเดิมซ้ำอีกรอบ ทั้งที่เพิ่งดูไปแล้ว (เหรียญไม่ได้เพิ่มซ้ำ เพราะฝั่งเกม
  // แค่ "อ่าน" ผลที่บันทึกไว้แล้ว — แต่หน้าจอที่โผล่ซ้ำเองก็ชวนงงพอกัน)
  //
  // replaceState ไม่ทับประวัติของแท็บ ปุ่ม back จึงยังพากลับเว็บบำบัดได้ตามปกติ
  TictaClearUrlQuery: function () {
    try {
      window.history.replaceState(null, "", window.location.pathname);
    } catch (e) {
      // บาง sandbox (file://, iframe ข้ามโดเมน) ห้ามแตะ history — ไม่ใช่เรื่องคอขาดบาดตาย
    }
  },
});
