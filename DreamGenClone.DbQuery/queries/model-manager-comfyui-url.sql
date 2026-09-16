SELECT p.Name, p.BaseUrl, p.IsEnabled FROM Providers p WHERE p.Name LIKE '%ComfyUI%' OR p.Name LIKE '%local%' OR p.Name LIKE '%Qwen%' ORDER BY p.Name;
