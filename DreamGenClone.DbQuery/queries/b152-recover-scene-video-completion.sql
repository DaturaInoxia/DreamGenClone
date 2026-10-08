UPDATE SceneVideos
SET Status = 2,
    FileRelativePath = '11980a46-afa7-4f05-966b-6074fb565847/11980a46-afa7-4f05-966b-6074fb565847_00001_.mp4',
    NormalizedFileRelativePath = '11980a46-afa7-4f05-966b-6074fb565847/11980a46-afa7-4f05-966b-6074fb565847_00001__norm.mp4',
    VideoStreamPresent = 1,
    AudioStreamPresent = 1,
    MeasuredLoudnessLufs = -15.53,
    LoudnessTargetLufs = -16,
    MeasuredDurationSeconds = 5.20,
    VerificationNotes = 'Recovered 2026-10-07: the render and the loudness normalization both succeeded, but the completion UPDATE threw on an unbound SQL parameter ($rendering). The bind is fixed in SceneVideoRepository.TryCompleteAsync and these values were measured from the stored files with the model configured ffmpeg.',
    ErrorMessage = NULL,
    CompletedUtc = '2026-10-07T16:55:30.0000000Z',
    UpdatedUtc = '2026-10-07T17:35:21.5279579Z'
WHERE Id = '11980a46-afa7-4f05-966b-6074fb565847'
  AND Status = 3;
