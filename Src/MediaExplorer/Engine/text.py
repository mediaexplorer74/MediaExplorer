import os

def count_lines_in_folder(folder_path):
    total_lines = 0
    for root, _, files in os.walk(folder_path):
        for file in files:
            file_path = os.path.join(root, file)
            try:
                with open(file_path, 'r', encoding='utf-8', errors='ignore') as f:
                    line_count = sum(1 for _ in f)
                    print(f"{file_path} → {line_count} lines")
                    total_lines += line_count
            except Exception as e:
                print(f"⚠️ Could not read {file_path}: {e}")
    print(f"\n📊 Total lines in folder '{folder_path}': {total_lines}")

if __name__ == "__main__":
    folder = input("Enter folder path: ").strip()
    count_lines_in_folder(folder)
