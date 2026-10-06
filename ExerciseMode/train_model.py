"""
train_model.py — Siamese network วัดความคล้ายของท่าจาก keypoint 99 ค่า (33 จุด x xyz)

โครงสร้าง: base 99 -> Dense32 relu -> Dropout .3 -> Dense16 relu  แล้ววัด L2 ระหว่าง
embedding สองฝั่ง เทรนด้วย contrastive loss (margin 1.0) → ระยะใกล้ = ท่าเหมือน

⚠️ ไฟล์นี้ถูก import โดย main.py และ scripts/export_web.py เพื่อเอา custom object
ไปโหลดโมเดล  **ห้ามเขียนไฟล์โมเดลนอก __main__** รุ่นก่อนวาง save()/TFLiteConverter
ไว้ระดับโมดูล ผลคือทุกครั้งที่ import โมเดลสุ่มจะเขียนทับโมเดลที่เทรนแล้ว แล้ว main.py
ก็โหลดน้ำหนักสุ่มนั้นขึ้นมาเสิร์ฟ (วัดได้ ~0.5 = เท่าเดา) โดยไม่มี error บอก

รัน:  python train_model.py
"""
import numpy as np
import tensorflow as tf
from keras import layers, Model

SEED = 0
INPUT_DIM = 99
MARGIN = 1.0
DECISION_THRESHOLD = 0.3   # ระยะที่ถือว่า "ท่าเดียวกัน" — ต้องตรงกับฝั่ง serve/web


# === Custom objects ========================================================
# ต้อง register ไว้เพื่อให้ load_model หาเจอโดยไม่ต้องส่ง custom_objects เอง
# (ยังส่งให้อยู่ในฝั่ง main.py เพื่อความชัดเจน)

@tf.keras.utils.register_keras_serializable()
class L2Distance(tf.keras.layers.Layer):
    def call(self, inputs):
        x, y = inputs
        return tf.sqrt(tf.reduce_sum(tf.square(x - y), axis=1, keepdims=True) + 1e-6)


@tf.keras.utils.register_keras_serializable()
def contrastive_loss(y_true, y_pred):
    # reshape ก่อน: y_true เข้ามาเป็น (batch,) แต่ y_pred เป็น (batch, 1)
    # ถ้าไม่จัดรูป broadcasting จะพ่น (batch, batch) แล้ว loss เพี้ยนเงียบ ๆ
    y_true = tf.reshape(tf.cast(y_true, y_pred.dtype), (-1, 1))
    return tf.reduce_mean(
        y_true * tf.square(y_pred) +
        (1 - y_true) * tf.square(tf.maximum(MARGIN - y_pred, 0))
    )


@tf.keras.utils.register_keras_serializable()
def siamese_accuracy(y_true, y_pred):
    y_true = tf.reshape(tf.cast(y_true, y_pred.dtype), (-1, 1))
    return tf.reduce_mean(tf.cast(
        tf.equal(tf.cast(y_true, tf.bool), tf.less_equal(y_pred, DECISION_THRESHOLD)),
        tf.float32))


# === Model =================================================================

def build_base_model(input_shape=(INPUT_DIM,)):
    inputs = layers.Input(shape=input_shape)
    x = layers.Dense(32, activation='relu',
                     kernel_regularizer=tf.keras.regularizers.l2(0.001))(inputs)
    x = layers.Dropout(0.3)(x)
    x = layers.Dense(16, activation='relu',
                     kernel_regularizer=tf.keras.regularizers.l2(0.001))(x)
    return Model(inputs, x, name='base')


def build_siamese():
    base = build_base_model()
    a = layers.Input(shape=(INPUT_DIM,))
    b = layers.Input(shape=(INPUT_DIM,))
    distance = L2Distance(name='l2_distance')([base(a), base(b)])
    return Model(inputs=[a, b], outputs=distance), base


def l2_rows(x):
    """normalise ทีละแถวให้ยาว 1 — ต้องทำเหมือนกันทั้งตอนเทรน ตอน serve และบนเว็บ"""
    return x / (np.linalg.norm(x, axis=1, keepdims=True) + 1e-6)


def load_pairs():
    X1 = l2_rows(np.load('X1.npy'))
    X2 = l2_rows(np.load('X2.npy'))
    y = np.load('y.npy')
    assert set(np.unique(y)) == {0, 1}, 'y ต้องมีแค่ 0 กับ 1'
    for name, a in [('X1', X1), ('X2', X2)]:
        assert np.isfinite(a).all(), f'{name} มี NaN/inf'
    return X1, X2, y


def _report(model, X1, X2, y, label):
    d = model.predict([X1, X2], verbose=0).flatten()
    grid = np.linspace(0, 1.5, 301)
    accs = [((d <= t) == (y > 0.5)).mean() for t in grid]
    best = int(np.argmax(accs))
    print(f'{label:5s} n={len(y):5d}  acc@{DECISION_THRESHOLD}={((d <= DECISION_THRESHOLD) == (y > 0.5)).mean():.3f}'
          f'  best={accs[best]:.3f}@thr={grid[best]:.3f}'
          f'  ระยะเฉลี่ย same={d[y > 0.5].mean():.3f} diff={d[y < 0.5].mean():.3f}')


if __name__ == '__main__':
    tf.keras.utils.set_random_seed(SEED)
    X1, X2, y = load_pairs()

    # ต้องสุ่มสลับก่อนแบ่ง — y.npy เรียงคลาส 1 ทั้งก้อนไว้หน้า แล้ว 0 ทั้งก้อนไว้หลัง
    # ถ้าใช้ validation_split ของ Keras (ตัดท้ายโดยไม่สุ่ม) validation จะเป็นคลาส 0
    # ล้วน แล้วตัวเลข val ที่ได้จะไม่มีความหมาย
    idx = np.random.default_rng(SEED).permutation(len(y))
    X1, X2, y = X1[idx], X2[idx], y[idx]
    cut = int(len(y) * 0.8)
    tr = slice(0, cut)
    va = slice(cut, None)
    print(f'train {cut} / val {len(y) - cut}  (สัดส่วนคลาส 1 ใน val = {y[va].mean():.2f})')

    model, base = build_siamese()
    model.compile(optimizer=tf.keras.optimizers.Adam(learning_rate=1e-3),
                  loss=contrastive_loss, metrics=[siamese_accuracy])
    model.fit([X1[tr], X2[tr]], y[tr],
              batch_size=32, epochs=150, verbose=2,
              validation_data=([X1[va], X2[va]], y[va]),
              callbacks=[tf.keras.callbacks.EarlyStopping(
                  monitor='val_loss', patience=20, restore_best_weights=True)])

    print()
    _report(model, X1[tr], X2[tr], y[tr], 'train')
    _report(model, X1[va], X2[va], y[va], 'val')

    model.save('siamese_model.keras')
    base.save('siamese_base.keras')
    with open('siamese_model.tflite', 'wb') as f:
        f.write(tf.lite.TFLiteConverter.from_keras_model(model).convert())
    print('\nบันทึก siamese_model.keras / siamese_base.keras / siamese_model.tflite แล้ว')
