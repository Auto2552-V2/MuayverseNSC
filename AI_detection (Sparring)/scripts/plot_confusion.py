"""
วาด confusion matrix จากโมเดลตัวที่ใช้งานจริง (pose_action.h5) เป็นรูปสำหรับ README

    .venv\\Scripts\\python.exe scripts\\plot_confusion.py

ได้ docs/confusion_{val,test}_{light,dark}.png

ทำไมไม่ใช้รูปใน LSTM learn/Classification_Pose.ipynb: notebook เทรนโมเดลของตัวเอง
ในหน่วยความจำแล้วไม่เคย save — รูปในนั้นได้ test 95.6% ส่วนโมเดลที่เกมใช้จริง
(จาก train_pose_model.py) ได้ 93.3% เอารูปนั้นมาใส่ README ตัวเลขจะขัดกับตารางเอง

สคริปต์นี้คำนวณใหม่ทุกครั้งจากไฟล์โมเดลกับข้อมูลชุดเดียวกับตอนเทรน (โหลดผ่าน
train_pose_model.load_sessions) เทรนใหม่แล้วรันซ้ำ รูปจะตรงกับโมเดลเสมอ

ทำสองธีมเพราะ GitHub แสดง README ทั้งพื้นสว่างและพื้นมืด — README ใช้ <picture>
เลือกรูปตามธีมของคนดู
"""
import importlib.util
import os
import sys

import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
sys.path.insert(0, os.path.join(ROOT, 'scripts'))

# ── สี: ramp ฟ้าไล่จากอ่อนไปเข้ม (sequential = สีเดียว ไม่ใช่รุ้ง) ───────────────
BLUE = ['#cde2fb', '#b7d3f6', '#9ec5f4', '#86b6ef', '#6da7ec', '#5598e7', '#3987e5',
        '#2a78d6', '#256abf', '#1c5cab', '#184f95', '#104281', '#0d366b']

THEMES = {
    # บนพื้นสว่าง มากกว่า = เข้มกว่า
    'light': dict(surface='#fcfcfb', ink='#0b0b0b', ink2='#52514e', muted='#898781',
                  ramp=BLUE, more='เข้ม'),
    # บนพื้นมืดกลับทิศ มากกว่า = สว่างกว่า — ถ้าใช้ทิศเดิม ช่องที่มีค่ามากจะจมหาย
    # ไปกับพื้นหลัง และช่องที่เป็นศูนย์จะดูเด่นที่สุดซึ่งตรงข้ามกับความหมาย
    'dark': dict(surface='#1a1a19', ink='#ffffff', ink2='#c3c2b7', muted='#898781',
                 ramp=BLUE[::-1][1:],    # เริ่มที่ 650 ไม่ใช่ 700 ให้ค่าน้อยยังแยกจากพื้นได้
                 more='สว่าง'),
}

FONT = 'Leelawadee UI'   # มีอักษรไทย — ฟอนต์ดีฟอลต์ของ matplotlib จะขึ้นเป็นกล่อง


def evaluate():
    spec = importlib.util.spec_from_file_location(
        'train_pose_model', os.path.join(ROOT, 'scripts', 'train_pose_model.py'))
    tp = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(tp)
    from pose_features import ACTIONS

    os.environ.setdefault('TF_CPP_MIN_LOG_LEVEL', '3')
    import tensorflow as tf
    from sklearn.metrics import confusion_matrix

    model = tf.keras.models.load_model(os.path.join(ROOT, 'pose_action.h5'), compile=False)
    data = os.path.join(ROOT, 'LSTM learn', 'MP_Data_Diag')

    out = {}
    # ไม่ขยายข้อมูล (shift) ตรงนี้ — ต้องวัดแบบเดียวกับที่ train_pose_model.py วัด val/test
    for name, sessions in (('val', [2]), ('test', [3])):
        X, y = tp.load_sessions(data, sessions)
        t = np.argmax(y, axis=1)
        p = np.argmax(model.predict(X, verbose=0), axis=1)
        cm = confusion_matrix(t, p, labels=list(range(len(ACTIONS))))
        out[name] = (cm, float((t == p).mean()), sessions)
        print(f'{name:<5} n={len(t)}  acc={(t == p).mean():.1%}')
    return ACTIONS, out


def color_for(value, vmax, ramp):
    # ค่าที่น้อยที่สุดซึ่งไม่ใช่ศูนย์ได้ step แรกของ ramp ไม่ใช่สีพื้น — ความผิดพลาด
    # ครั้งเดียวต้องมองเห็นได้ เพราะมันคือสิ่งที่คนดูรูปนี้อยากหา
    i = round((value - 1) / max(vmax - 1, 1) * (len(ramp) - 1))
    return ramp[int(np.clip(i, 0, len(ramp) - 1))]


def readable_on(hex_color, light_ink, dark_ink):
    r, g, b = (int(hex_color[i:i + 2], 16) / 255 for i in (1, 3, 5))
    lum = 0.2126 * r + 0.7152 * g + 0.0722 * b
    return dark_ink if lum > 0.45 else light_ink


def draw(actions, cm, acc, sessions, label, theme, path):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    from matplotlib.patches import FancyBboxPatch

    th = THEMES[theme]
    plt.rcParams['font.family'] = FONT
    n = len(actions)
    vmax = int(cm.max())

    fig, ax = plt.subplots(figsize=(7.2, 6.6), dpi=200)
    fig.patch.set_facecolor(th['surface'])
    ax.set_facecolor(th['surface'])

    gap = 0.04   # ช่องว่างสีพื้นระหว่างเซลล์ แยกเซลล์ติดกันที่สีใกล้กันได้
    for r in range(n):
        for c in range(n):
            v = int(cm[r, c])
            x, y = c + gap / 2, r + gap / 2
            if v == 0:
                ax.text(c + .5, r + .5, '0', ha='center', va='center',
                        fontsize=11, color=th['muted'])
                continue
            fill = color_for(v, vmax, th['ramp'])
            ax.add_patch(FancyBboxPatch((x, y), 1 - gap, 1 - gap,
                                        boxstyle='round,pad=0,rounding_size=0.06',
                                        facecolor=fill, edgecolor='none'))
            ink = readable_on(fill, '#ffffff', '#0b0b0b')
            ax.text(c + .5, r + .5, str(v), ha='center', va='center', fontsize=14,
                    color=ink, fontweight='bold' if r == c else 'normal')

    ax.set_xlim(0, n)
    ax.set_ylim(n, 0)
    ax.set_aspect('equal')
    ax.set_xticks(np.arange(n) + .5)
    ax.set_yticks(np.arange(n) + .5)
    ax.set_xticklabels(actions, fontsize=11, color=th['ink2'])
    ax.set_yticklabels(actions, fontsize=11, color=th['ink2'])
    ax.xaxis.tick_top()
    ax.xaxis.set_label_position('top')
    ax.tick_params(length=0, pad=6)
    for s in ax.spines.values():
        s.set_visible(False)

    ax.set_xlabel('ท่าที่โมเดลทาย', fontsize=11.5, color=th['muted'], labelpad=10)
    ax.set_ylabel('ท่าที่ทำจริง', fontsize=11.5, color=th['muted'], labelpad=10)

    total = int(cm.sum())
    wrong = total - int(np.trace(cm))
    fig.suptitle(f'Confusion matrix — {label}', x=0.08, y=0.975, ha='left',
                 fontsize=15, fontweight='bold', color=th['ink'])
    # อักษรไทยมีสระ/วรรณยุกต์ห้อยลงล่าง เว้นจากหัวเรื่องมากกว่าที่ข้อความอังกฤษต้องใช้
    fig.text(0.08, 0.895,
             f'session {",".join(map(str, sessions))}  ·  {total} คลิป  ·  '
             f'ถูก {acc:.1%}  ·  ผิด {wrong} คลิป',
             ha='left', fontsize=11, color=th['ink2'])
    fig.text(0.08, 0.035,
             f'ทแยงมุม = ทายถูก  ·  ช่องนอกทแยง = ทายผิด  ·  สี{th["more"]}ขึ้นตามจำนวนคลิป',
             ha='left', fontsize=9.5, color=th['muted'])

    fig.subplots_adjust(left=0.17, right=0.95, top=0.77, bottom=0.08)
    fig.savefig(path, facecolor=th['surface'])
    plt.close(fig)


def main():
    actions, results = evaluate()
    out_dir = os.path.join(ROOT, 'docs')
    os.makedirs(out_dir, exist_ok=True)
    labels = {'val': 'ชุด validation', 'test': 'ชุด test (โมเดลไม่เคยเห็น)'}
    for name, (cm, acc, sessions) in results.items():
        for theme in THEMES:
            p = os.path.join(out_dir, f'confusion_{name}_{theme}.png')
            draw(actions, cm, acc, sessions, labels[name], theme, p)
            print('wrote', os.path.relpath(p, ROOT))


if __name__ == '__main__':
    main()
