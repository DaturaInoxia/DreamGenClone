SELECT v.Id                AS VideoId,
       v.Title             AS Title,
       v.Status            AS VideoStatus,
       v.OriginKind        AS OriginKind,
       v.OriginImageId     AS OriginImageId,
       v.CompilerKey       AS CompilerKey,
       v.CompilerVersion   AS CompilerVersion,
       v.CreatedUtc        AS VideoCreatedUtc,
       v.StartedUtc        AS VideoStartedUtc,
       v.CompletedUtc      AS VideoCompletedUtc,
       v.JobId             AS JobId,
       j.Status            AS JobStatus,
       j.Lane              AS JobLane,
       j.AttemptCount      AS Attempts,
       j.MaxAttempts       AS MaxAttempts,
       j.LeaseOwner        AS LeaseOwner,
       j.LeaseExpiresUtc   AS LeaseExpiresUtc,
       j.ErrorCode         AS ErrorCode,
       j.ErrorMessage      AS ErrorMessage,
       j.UpdatedUtc        AS JobUpdatedUtc,
       length(v.PromptSnapshot) AS PromptChars,
       v.FileRelativePath  AS FileRelativePath,
       v.VideoStreamPresent AS HasVideoStream,
       v.AudioStreamPresent AS HasAudioStream,
       v.MeasuredLoudnessLufs AS MeasuredLufs,
       v.VerificationNotes AS VerificationNotes
FROM SceneVideos v
LEFT JOIN DurableBackgroundJobs j ON j.Id = v.JobId
WHERE v.OriginImageId = '{{id}}'
   OR v.Id = '{{id}}'
ORDER BY v.CreatedUtc DESC
LIMIT 5;
