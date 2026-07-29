import os
import shutil
import sys

def fix_nested_folders(folder_path):
    if not os.path.exists(folder_path):
        print(f"❌ المجلد غير موجود: {folder_path}")
        return

    print(f"🔍 فحص وتصفية المجلدات المتداخلة داخل: {folder_path} ...\n")

    direct_dirs = [d for d in os.listdir(folder_path) if os.path.isdir(os.path.join(folder_path, d))]
    print(f"📊 عدد العناصر في المجلد الرئيسي: {len(direct_dirs)}")

    fixed_count = 0
    removed_count = 0

    for d in list(direct_dirs):
        parent_dir = os.path.join(folder_path, d)
        
        try:
            sub_dirs = [s for s in os.listdir(parent_dir) if os.path.isdir(os.path.join(parent_dir, s))]
        except Exception:
            continue

        for s in sub_dirs:
            nested_path = os.path.join(parent_dir, s)
            target_path = os.path.join(folder_path, s)

            # Check if this is a valid panel folder with info.el or row.tif
            if os.path.exists(os.path.join(nested_path, "info.el")) or os.path.exists(os.path.join(nested_path, "row.tif")):
                print(f"⚡ وجد مجلد مكرر/متداخل: {s} (داخل {d})")

                nested_mtime = os.path.getmtime(nested_path)

                if os.path.exists(target_path):
                    target_mtime = os.path.getmtime(target_path)
                    if nested_mtime > target_mtime:
                        print(f"  --> المجلد المتداخل أحدث معدل. استبدال المجلد القديم...")
                        shutil.rmtree(target_path)
                        shutil.move(nested_path, target_path)
                        fixed_count += 1
                    else:
                        print(f"  --> المجلد الرئيسي هو الأحدث. حذف المجلد المتداخل القديم...")
                        shutil.rmtree(nested_path)
                        removed_count += 1
                else:
                    print(f"  --> نقل المجلد المتداخل إلى المجلد الرئيسي...")
                    shutil.move(nested_path, target_path)
                    fixed_count += 1

    # Cleanup empty wrapper directories
    for d in os.listdir(folder_path):
        wrapper_path = os.path.join(folder_path, d)
        if os.path.isdir(wrapper_path):
            has_info = os.path.exists(os.path.join(wrapper_path, "info.el"))
            has_tif = os.path.exists(os.path.join(wrapper_path, "row.tif"))
            if not has_info and not has_tif:
                try:
                    shutil.rmtree(wrapper_path)
                    print(f"🗑️ حذف المجلد الخالي التغليفي: {d}")
                except Exception:
                    pass

    final_dirs = [d for d in os.listdir(folder_path) if os.path.isdir(os.path.join(folder_path, d))]
    print("\n==========================================")
    print("🏁 اكتمل تصليح وفرد المجلدات بنجاح!")
    print(f"📁 العدد النهائي الصافي للمجلدات: {len(final_dirs)}")
    print(f"✨ تم تحديث/نقل {fixed_count} مجلد أحدث")
    print(f"🗑️ تم تنظيف/حذف {removed_count} مجلد قديم مكرر")
    print("==========================================")

if __name__ == "__main__":
    if len(sys.argv) > 1:
        target = sys.argv[1]
    else:
        target = input("أدخل مسار مجلد Re_evaluation: ").strip('"\'')
    
    fix_nested_folders(target)
