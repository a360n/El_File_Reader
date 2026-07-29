# process_tif.py
import os
from PIL import Image

# ==============================================================================
# ضع مسار المجلد الرئيسي هنا يدوياً بين العلامتين r"..."
# مثال: r"C:\Users\Username\Pictures\MyFolder"
# ==============================================================================
TARGET_FOLDER = r"C:\path\to\your\folder"


def process_tif_files(folder_path):
    # التأكد من وجود المجلد
    if not os.path.exists(folder_path):
        print(f"❌ المجلد غير موجود: {folder_path}")
        return

    processed_count = 0
    modified_count = 0
    error_count = 0

    print(f"🔍 جاري البحث عن ملفات TIF داخل: {folder_path} ...\n")

    # os.walk للبحث الشامل في المجلد وجميع المجلدات الفرعية
    for root, dirs, files in os.walk(folder_path):
        for file in files:
            # فحص صيغ الملفات .tif و .tiff (غير حساس لحالة الأحرف)
            if file.lower().endswith(('.tif', '.tiff')):
                file_path = os.path.join(root, file)
                processed_count += 1

                try:
                    with Image.open(file_path) as img:
                        width, height = img.size

                        # 1. فحص الارتفاع: إذا كان فردياً ينقص 1 ليصبح زوجياً
                        new_height = height if height % 2 == 0 else height - 1

                        # 2. فحص العرض: نصف الارتفاع الجديد
                        target_max_width = new_height // 2

                        # إذا كان العرض أكبر من نصف الارتفاع، نقصه إلى نصف الارتفاع
                        new_width = min(width, target_max_width)

                        # فحص هل تحتاج الصورة لتعديل بالفعل؟
                        if new_width != width or new_height != height:
                            # قص الصورة من اليمين والأسفل (اليسار=0, الأعلى=0, اليمين=new_width, الأسفل=new_height)
                            cropped_img = img.crop((0, 0, new_width, new_height))

                            # حفظ التعديل عبر ملف مؤقت لتفادي مشاكل قفل الملفات في ويندوز
                            temp_path = file_path + ".tmp"
                            cropped_img.save(temp_path, format=img.format)
                            img.close()  # إغلاق الصورة الأصلية للتمكن من استبدالها

                            os.replace(temp_path, file_path)

                            modified_count += 1
                            print(f"✅ [تم التعديل] {file_path}")
                            print(f"   القياس القديم: {width}x{height} ⬅️ القياس الجديد: {new_width}x{new_height}")
                        else:
                            print(f"ℹ️ [بدون تغيير] {file_path} (الأبعاد: {width}x{height})")

                except Exception as e:
                    error_count += 1
                    print(f"❌ [خطأ] تعذر معالجة الملف: {file_path}")
                    print(f"   السبب: {e}")

    # ملخص العملية
    print("\n==========================================")
    print(f"🏁 اكتملت المعالجة!")
    print(f"📁 إجمالي ملفات TIF التي تم فحصها: {processed_count}")
    print(f"✂️ عدد الملفات التي تم تعديلها: {modified_count}")
    print(f"⚠️ عدد الملفات التي حدث بها خطأ: {error_count}")
    print("==========================================")


if __name__ == "__main__":
    process_tif_files(TARGET_FOLDER)