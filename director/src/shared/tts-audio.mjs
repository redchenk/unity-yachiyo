// GPT-SoVITS streaming WAV starts with an empty RIFF/data header followed by PCM.
// Chromium tolerates it; WebKit requires finite lengths before media playback.
export function completeTtsWav(input) {
  const bytes = input instanceof Uint8Array ? input : new Uint8Array(input);
  const tag = at => String.fromCharCode(...bytes.subarray(at, at + 4));
  if (tag(0) !== 'RIFF' || tag(8) !== 'WAVE') return bytes;
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const invalid = () => new Error('语音 WAV 数据不完整，请重试合成');
  let blockAlign = 0;
  for (let at = 12; at + 8 <= bytes.length;) {
    const size = view.getUint32(at + 4, true), start = at + 8;
    if (tag(at) === 'fmt ' && size >= 16 && start + size <= bytes.length) {
      blockAlign = view.getUint16(start + 12, true);
    }
    if (tag(at) === 'data') {
      if (!blockAlign) throw invalid();
      if (size === 0 || size === 0xffffffff) {
        const length = bytes.length - start;
        if (!length || length % blockAlign) throw invalid();
        const result = Uint8Array.from(bytes), header = new DataView(result.buffer);
        header.setUint32(4, result.length - 8, true);
        header.setUint32(at + 4, length, true);
        return result;
      }
      if (start + size > bytes.length || size % blockAlign || view.getUint32(4, true) + 8 > bytes.length) throw invalid();
      return bytes;
    }
    if (start + size > bytes.length) throw invalid();
    at = start + size + (size % 2);
  }
  throw invalid();
}

export async function playableTtsBlob(blob) {
  if (!blob.size) throw new Error('语音服务返回了空音频');
  if (/json|text\//i.test(blob.type)) throw new Error('语音服务返回的不是音频，请检查语音服务设置');
  const source = new Uint8Array(await blob.arrayBuffer());
  const complete = completeTtsWav(source);
  const wav = source[0] === 82 && source[1] === 73 && source[2] === 70 && source[3] === 70;
  return new Blob([complete], { type: wav ? 'audio/wav' : (blob.type || 'application/octet-stream') });
}
