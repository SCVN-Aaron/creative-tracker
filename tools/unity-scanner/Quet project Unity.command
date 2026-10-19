#!/bin/bash
# macOS: bấm đúp file này, kéo thả thư mục project Unity vào cửa sổ Terminal rồi Enter.
cd "$(dirname "$0")"
read -r -p "Kéo thả thư mục project Unity vào đây rồi Enter: " P
P="${P%"${P##*[![:space:]]}"}"      # bỏ khoảng trắng cuối
P="${P//\\ / }"                     # Finder thêm "\ " cho dấu cách
P="${P#\'}"; P="${P%\'}"
python3 unity_scan.py "$P" --open
read -r -p "Xong. Enter để đóng."
