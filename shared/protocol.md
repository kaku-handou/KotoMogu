# 通信の約束（protocol）

## 1. 通信の方法
- UDP、同じPC内（127.0.0.1）
- Python → Unity：ポート 5005（Unity が受け取る）
- Unity → Python：ポート 5006（Python が受け取る）
- 1回の送信 = JSON 1つ（UTF-8）

## 2. 共通のルール
- すべてのメッセージに "type" を入れる
- 向きはすべて「こともぐから見た向き」。右がプラス、上がプラス
- 知らない type や知らない動きの名前が来たら、警告を出して無視する（止まらない）

## 3. Python → Unity

### play（命令）：動きを再生する
```json
{"type": "play", "action": "greet"}
```
- action：動きの名前。Blender のアクション名と同じ

### look（状態）：首を向ける方向
```json
{"type": "look", "yaw": 0.3, "pitch": -0.1}
```
- yaw：左右。-1（左いっぱい）〜 1（右いっぱい）
- pitch：上下。-1（下いっぱい）〜 1（上いっぱい）
- 1秒に約30回送る

## 4. Unity → Python

### finished（予定）：一度きりの動きが終わった
```json
{"type": "finished", "action": "greet"}
```

## 5. 変更の記録
- 2026-11-18：作成。play と look を決めた