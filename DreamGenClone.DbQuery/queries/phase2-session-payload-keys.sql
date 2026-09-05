SELECT key, type
FROM Sessions, json_each(PayloadJson)
WHERE Sessions.Id = '{{id}}';
