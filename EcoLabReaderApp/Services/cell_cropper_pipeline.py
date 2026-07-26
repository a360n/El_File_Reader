#!/usr/bin/env python3
"""
EcoLAB AI Cell Cropper & Computer Vision Dataset Pipeline
---------------------------------------------------------
Performs:
1. Image Alignment & Perspective Transform (Canny Edge Detection & WarpPerspective on raw `row.tif`)
2. Outer Frame Removal
3. Grid Calculation (6 Columns A-F x 24 Rows 01-24 = 144 Cells)
4. Cropping with 12% Padding Overlap + Reflection Padding (BORDER_REFLECT_101) for Corner & Edge Cells
5. Resizing (224x224) & Structured Dataset Export to AICell/
"""

import os
import sys
import glob
import json
import shutil
import re
import cv2
import numpy as np

def parse_info_el_defects(info_el_path):
    """
    Parses info.el to extract defective cell locations (e.g. ['D13', 'E05', 'B4']).
    Returns set of normalized defect cell keys (e.g. {'D13', 'E05', 'E5', 'B04', 'B4'}).
    """
    defects = set()
    if not os.path.exists(info_el_path):
        return defects

    try:
        with open(info_el_path, 'r', encoding='utf-8', errors='ignore') as f:
            content = f.read()

        # Match defect patterns like D13, A01, B4, F24
        matches = re.findall(r'\b([A-F])([0-9]{1,2})\b', content)
        for col, row in matches:
            row_int = int(row)
            if 1 <= row_int <= 24:
                defects.add(f"{col}{row_int}")
                defects.add(f"{col}{row_int:02d}")
    except Exception as e:
        print(f"Warning: Could not parse {info_el_path}: {e}")

    return defects

def order_points(pts):
    """Orders 4 corner points: top-left, top-right, bottom-right, bottom-left."""
    rect = np.zeros((4, 2), dtype="float32")
    s = pts.sum(axis=1)
    rect[0] = pts[np.argmin(s)] # Top-left
    rect[2] = pts[np.argmax(s)] # Bottom-right

    diff = np.diff(pts, axis=1)
    rect[1] = pts[np.argmin(diff)] # Top-right
    rect[3] = pts[np.argmax(diff)] # Bottom-left
    return rect

def align_and_rectify_panel(image):
    """
    Performs Image Alignment & Perspective Transform using OpenCV edge detection.
    Falls back gracefully to tight margin crop if contour detection finds full bounding box.
    """
    if len(image.shape) == 3:
        gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    else:
        gray = image.copy()

    h, w = gray.shape[:2]

    # Blur & Canny Edge Detection
    blurred = cv2.GaussianBlur(gray, (5, 5), 0)
    edges = cv2.Canny(blurred, 30, 150)

    # Find external contours
    contours, _ = cv2.findContours(edges, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    
    rectified = None
    if contours:
        # Find largest contour by area
        contours = sorted(contours, key=cv2.contourArea, reverse=True)
        for c in contours:
            peri = cv2.arcLength(c, True)
            approx = cv2.approxPolyDP(c, 0.02 * peri, True)

            # If contour has 4 points and covers significant area
            if len(approx) == 4 and cv2.contourArea(c) > (w * h * 0.4):
                pts = approx.reshape(4, 2)
                rect_pts = order_points(pts)

                tl, tr, br, bl = rect_pts
                widthA = np.sqrt(((br[0] - bl[0]) ** 2) + ((br[1] - bl[1]) ** 2))
                widthB = np.sqrt(((tr[0] - tl[0]) ** 2) + ((tr[1] - tl[1]) ** 2))
                maxWidth = max(int(widthA), int(widthB))

                heightA = np.sqrt(((tr[0] - br[0]) ** 2) + ((tr[1] - br[1]) ** 2))
                heightB = np.sqrt(((tl[0] - bl[0]) ** 2) + ((tl[1] - bl[1]) ** 2))
                maxHeight = max(int(heightA), int(heightB))

                dst = np.array([
                    [0, 0],
                    [maxWidth - 1, 0],
                    [maxWidth - 1, maxHeight - 1],
                    [0, maxHeight - 1]
                ], dtype="float32")

                M = cv2.getPerspectiveTransform(rect_pts, dst)
                rectified = cv2.warpPerspective(image, M, (maxWidth, maxHeight))
                break

    # Fallback if no clean polygon found: Crop outer 1% frame
    if rectified is None:
        pad_y = int(h * 0.01)
        pad_x = int(w * 0.01)
        rectified = image[pad_y:h-pad_y, pad_x:w-pad_x]

    return rectified

def process_panel_folder(folder_path, is_good_model, restructured_base, aicell_base):
    """
    Processes a single panel folder:
    - Loads raw `row.tif`
    - Rectifies, crops grid, applies reflection padding
    - Saves cells to 'all single cells' and 'defective single cells'
    - Aggregates cells into all_good_cells / all_bad_cells
    """
    folder_name = os.path.basename(folder_path)
    info_el_path = os.path.join(folder_path, "info.el")
    row_tif_path = os.path.join(folder_path, "row.tif")

    # Fallback image search if row.tif isn't named exactly row.tif
    if not os.path.exists(row_tif_path):
        tifs = glob.glob(os.path.join(folder_path, "*.tif"))
        row_tifs = [t for t in tifs if "marked" not in os.path.basename(t).lower()]
        if row_tifs:
            row_tif_path = row_tifs[0]
        elif tifs:
            row_tif_path = tifs[0]
        else:
            return {"folder": folder_name, "status": "skipped_no_image"}

    # Parse defects
    defect_keys = parse_info_el_defects(info_el_path)

    # Subfolders inside panel folder
    all_cells_dir = os.path.join(folder_path, "all single cells")
    defective_cells_dir = os.path.join(folder_path, "defective single cells")

    # Check if 'all single cells' exists and has 144 images
    existing_imgs = glob.glob(os.path.join(all_cells_dir, "*.png")) if os.path.exists(all_cells_dir) else []
    
    need_cropping = False
    if not os.path.exists(all_cells_dir) or len(existing_imgs) < 144:
        need_cropping = True
        if os.path.exists(all_cells_dir):
            shutil.rmtree(all_cells_dir)
        os.makedirs(all_cells_dir, exist_ok=True)

    if defect_keys:
        os.makedirs(defective_cells_dir, exist_ok=True)

    # Load RAW image
    raw_img = cv2.imread(row_tif_path)
    if raw_img is None:
        return {"folder": folder_name, "status": "failed_load_image"}

    # Alignment & Perspective Transform
    rectified = align_and_rectify_panel(raw_img)
    rh, rw = rectified.shape[:2]

    # Grid calculations (6 Columns x 24 Rows)
    cols = ['A', 'B', 'C', 'D', 'E', 'F']
    cell_w = rw / 6.0
    cell_h = rh / 24.0

    # Overlap / Safety Padding (12%)
    pad_x = int(round(cell_w * 0.12))
    pad_y = int(round(cell_h * 0.12))

    # Apply Reflection Padding around entire rectified module to seamlessly handle Outer Edges & Corners
    padded_rectified = cv2.copyMakeBorder(rectified, pad_y, pad_y, pad_x, pad_x, cv2.BORDER_REFLECT_101)

    # Global aggregation target directories
    good_models_dir = os.path.join(restructured_base, "Good_models")
    bad_models_dir = os.path.join(restructured_base, "bad_models")

    global_good_cells_dir = os.path.join(good_models_dir, "all_good_cells")
    global_bad_cells_dir = os.path.join(bad_models_dir, "all_bad_cells")

    aicell_good_cells_dir = os.path.join(aicell_base, "all_good_cells")
    aicell_bad_cells_dir = os.path.join(aicell_base, "all_bad_cells")

    os.makedirs(global_good_cells_dir, exist_ok=True)
    os.makedirs(global_bad_cells_dir, exist_ok=True)
    os.makedirs(aicell_good_cells_dir, exist_ok=True)
    os.makedirs(aicell_bad_cells_dir, exist_ok=True)

    total_good = 0
    total_bad = 0

    for c_idx, col_name in enumerate(cols):
        for r_idx in range(1, 25):
            cell_label = f"{col_name}{r_idx:02d}"
            short_cell_label = f"{col_name}{r_idx}"

            is_defective = (cell_label in defect_keys) or (short_cell_label in defect_keys)
            status_str = "bad" if is_defective else "good"
            cell_filename = f"{short_cell_label}_{folder_name}_{status_str}.png"

            cell_path_in_panel = os.path.join(all_cells_dir, cell_filename)

            if need_cropping:
                # Compute base bounding box
                x_min = int(round(c_idx * cell_w))
                x_max = int(round((c_idx + 1) * cell_w))
                y_min = int(round((r_idx - 1) * cell_h))
                y_max = int(round(r_idx * cell_h))

                # Coordinates in padded_rectified image (+pad_x, +pad_y offset)
                px1 = x_min
                px2 = x_max + 2 * pad_x
                py1 = y_min
                py2 = y_max + 2 * pad_y

                cell_crop = padded_rectified[py1:py2, px1:px2]

                # Resize to standard uniform dimension (224x224)
                resized_cell = cv2.resize(cell_crop, (224, 224), interpolation=cv2.INTER_AREA)

                # Save cropped cell
                cv2.imwrite(cell_path_in_panel, resized_cell)

            # Copy to panel's 'defective single cells' folder if defective
            if is_defective:
                total_bad += 1
                defective_cell_dest = os.path.join(defective_cells_dir, cell_filename)
                if os.path.exists(cell_path_in_panel):
                    shutil.copy2(cell_path_in_panel, defective_cell_dest)

                # Global bad cells aggregation
                if os.path.exists(cell_path_in_panel):
                    shutil.copy2(cell_path_in_panel, os.path.join(global_bad_cells_dir, cell_filename))
                    shutil.copy2(cell_path_in_panel, os.path.join(aicell_bad_cells_dir, cell_filename))
            else:
                total_good += 1
                # Global good cells aggregation
                if os.path.exists(cell_path_in_panel):
                    shutil.copy2(cell_path_in_panel, os.path.join(global_good_cells_dir, cell_filename))
                    shutil.copy2(cell_path_in_panel, os.path.join(aicell_good_cells_dir, cell_filename))

    return {
        "folder": folder_name,
        "status": "success",
        "good_cells": total_good,
        "bad_cells": total_bad
    }

def run_pipeline(restructured_path, aicell_path):
    """Executes the complete dataset partitioning and AI cell cropping pipeline."""
    good_models_dir = os.path.join(restructured_path, "Good_models")
    bad_models_dir = os.path.join(restructured_path, "bad_models")

    aicell_good_models = os.path.join(aicell_path, "Good_models")
    aicell_bad_models = os.path.join(aicell_path, "bad_models")

    os.makedirs(aicell_good_models, exist_ok=True)
    os.makedirs(aicell_bad_models, exist_ok=True)

    results = []

    # Process Good Models
    if os.path.exists(good_models_dir):
        for item in os.listdir(good_models_dir):
            p = os.path.join(good_models_dir, item)
            if os.path.isdir(p) and item != "all_good_cells":
                res = process_panel_folder(p, is_good_model=True, restructured_base=restructured_path, aicell_base=aicell_path)
                results.append(res)
                # Copy entire panel folder to AICell/Good_models/
                dest_p = os.path.join(aicell_good_models, item)
                if os.path.exists(dest_p):
                    shutil.rmtree(dest_p)
                shutil.copytree(p, dest_p)

    # Process Bad Models
    if os.path.exists(bad_models_dir):
        for item in os.listdir(bad_models_dir):
            p = os.path.join(bad_models_dir, item)
            if os.path.isdir(p) and item != "all_bad_cells":
                res = process_panel_folder(p, is_good_model=False, restructured_base=restructured_path, aicell_base=aicell_path)
                results.append(res)
                # Copy entire panel folder to AICell/bad_models/
                dest_p = os.path.join(aicell_bad_models, item)
                if os.path.exists(dest_p):
                    shutil.rmtree(dest_p)
                shutil.copytree(p, dest_p)

    total_good = sum(r.get("good_cells", 0) for r in results if r.get("status") == "success")
    total_bad = sum(r.get("bad_cells", 0) for r in results if r.get("status") == "success")

    summary = {
        "success": True,
        "message": f"تمت عملية تقييم وتجزئة الخلايا بنجاح! تم معالجة {len(results)} لوح، واستخراج {total_good} خلية سليمة و {total_bad} خلية معيبة داخل مجلد AICell.",
        "total_panels": len(results),
        "total_good_cells": total_good,
        "total_bad_cells": total_bad,
        "details": results
    }

    return summary

if __name__ == "__main__":
    restructured = "/Users/alial-khazali/Documents/el file/Restructured"
    aicell = "/Users/alial-khazali/Documents/el file/AICell"

    if len(sys.argv) > 1:
        restructured = sys.argv[1]
    if len(sys.argv) > 2:
        aicell = sys.argv[2]

    summary = run_pipeline(restructured, aicell)
    print(json.dumps(summary, ensure_ascii=False, indent=2))
