window.chatInterop = {
  playAudio: (audioFile) => {
    const audio = new Audio(audioFile);
    audio.play().catch((e) => console.error("Audio playback failed:", e));
  },

  pauseAudio: () => {
    const audios = document.getElementsByTagName("audio");
    for (let audio of audios) {
      audio.pause();
    }
  },

  playAudioWithCallback: (audioFile, dotnetRef) => {
    const audio = new Audio(audioFile);
    audio.play().catch((e) => {
      console.error("Audio playback failed:", e);
      dotnetRef.invokeMethodAsync("OnAudioEnded");
    });
    audio.onended = () => {
      dotnetRef.invokeMethodAsync("OnAudioEnded");
      dotnetRef.dispose();
    };
  },

  recordingHandles: {},
  lastRecordingInfo: null,

  checkRecordingSupport: async function () {
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
      return false;
    }

    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      stream.getTracks().forEach((track) => track.stop());
      return true;
    } catch (err) {
      console.error("Recording not supported:", err);
      return false;
    }
  },

  startRecording: async function (sessionId) {
    console.log("startRecording called with sessionId:", sessionId);

    try {
      if (!this.recordingHandles) {
        this.recordingHandles = {};
      }

      if (this.recordingHandles[sessionId]) {
        console.log("Already recording this session");
        return false;
      }

      if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
        console.error("Media devices not supported");
        return false;
      }

      const stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          sampleRate: 16000,
          channelCount: 1,
          noiseSuppression: true,
          echoCancellation: true,
        },
      });
      console.log("Got media stream");

      const preferredTypes = [
        "audio/webm;codecs=opus",
        "audio/webm",
        "audio/ogg;codecs=opus",
        "audio/ogg",
        "audio/mp4",
      ];
      let chosenType = "";
      if (typeof MediaRecorder !== "undefined") {
        chosenType =
          preferredTypes.find((type) => MediaRecorder.isTypeSupported(type)) ||
          "";
      }
      const options = chosenType ? { mimeType: chosenType } : undefined;
      const mediaRecorder = new MediaRecorder(stream, options);
      const audioChunks = [];

      mediaRecorder.ondataavailable = function (event) {
        if (event.data.size > 0) {
          audioChunks.push(event.data);
          console.log("Data chunk added, size:", event.data.size);
        }
      };

      mediaRecorder.onerror = function (event) {
        console.error("MediaRecorder error:", event.error);
      };

      mediaRecorder.start(250);
      console.log("MediaRecorder started with type:", mediaRecorder.mimeType);

      this.recordingHandles[sessionId] = {
        mediaRecorder: mediaRecorder,
        audioChunks: audioChunks,
        stream: stream,
        mimeType: mediaRecorder.mimeType || chosenType || "audio/webm",
      };

      console.log("Recording started for session:", sessionId);
      console.log("Current recordingHandles:", Object.keys(this.recordingHandles));

      return true;
    } catch (error) {
      console.error("Error starting recording:", error);
      return false;
    }
  },

  stopRecording: async function (sessionId) {
    const recorderObj = window.chatInterop.recordingHandles?.[sessionId];
    if (!recorderObj) {
      console.error("No recorder found for sessionId:", sessionId);
      return null;
    }

    return new Promise((resolve) => {
      if (!recorderObj.mediaRecorder) {
        console.error("MediaRecorder not found");
        resolve(null);
        return;
      }

      const finalize = async () => {
        try {
          if (!recorderObj.audioChunks || recorderObj.audioChunks.length === 0) {
            console.error("No audio chunks");
            resolve(null);
            return;
          }

          const resolvedMimeType = recorderObj.mimeType || "audio/webm";
          const audioBlob = new Blob(recorderObj.audioChunks, {
            type: resolvedMimeType,
          });

          window.chatInterop.lastRecordingInfo = {
            mimeType: resolvedMimeType,
            extension: window.chatInterop.mimeTypeToExtension(resolvedMimeType),
          };

          if (recorderObj.stream) {
            recorderObj.stream.getTracks().forEach((track) => track.stop());
          }
          delete window.chatInterop.recordingHandles[sessionId];

          resolve(audioBlob);
        } catch (err) {
          console.error("Error handling audio data:", err);
          resolve(null);
        }
      };

      if (recorderObj.mediaRecorder.state !== "inactive") {
        recorderObj.mediaRecorder.onstop = finalize;

        try {
          if (typeof recorderObj.mediaRecorder.requestData === "function") {
            recorderObj.mediaRecorder.requestData();
          }
          recorderObj.mediaRecorder.stop();
        } catch (err) {
          console.error("Error stopping mediaRecorder:", err);
          resolve(null);
        }
      } else {
        console.log("MediaRecorder already inactive, creating blob immediately");
        finalize();
      }
    });
  },

  getLastRecordingInfo: function () {
    const info = this.lastRecordingInfo;
    this.lastRecordingInfo = null;
    return info;
  },

  mimeTypeToExtension: function (mimeType) {
    if (!mimeType) {
      return "webm";
    }
    const normalized = mimeType.toLowerCase();
    if (normalized.includes("ogg")) {
      return "ogg";
    }
    if (normalized.includes("mp4")) {
      return "mp4";
    }
    if (normalized.includes("webm")) {
      return "webm";
    }
    return "webm";
  },

  downloadFile: function (filename, content, contentType) {
    const blob = new Blob([content], {
      type: contentType || "application/octet-stream",
    });
    const url = URL.createObjectURL(blob);

    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();

    setTimeout(() => {
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    }, 100);
  },

  scrollToBottom: function (element) {
    element.scrollTop = element.scrollHeight;
  },
};
