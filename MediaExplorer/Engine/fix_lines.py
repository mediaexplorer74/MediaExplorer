
lines_to_delete_start = 4225
lines_to_delete_end = 4495
file_path = r"c:\Users\udayk\Videos\WEBVIEW\WEBVIEW\Engine\DomBasicRenderer.cs"

with open(file_path, 'r', encoding='utf-8-sig') as f:
    lines = f.readlines()

new_lines = []
for i, line in enumerate(lines):
    line_num = i + 1
    if line_num >= lines_to_delete_start and line_num <= lines_to_delete_end:
        continue
    new_lines.append(line)

with open(file_path, 'w', encoding='utf-8-sig') as f:
    f.writelines(new_lines)

print(f"Deleted lines {lines_to_delete_start} to {lines_to_delete_end}")
