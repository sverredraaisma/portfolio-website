import type { Block } from '~/types/blocks'

// Average adult silent reading speed for prose. Code reads slower; we count
// it at half-speed to keep the estimate honest.
const PROSE_WPM = 220
const CODE_WPM = 110

function wordCount(s: string): number {
  // Strip URLs (their character count overstates word count) and split on
  // any whitespace. Empty matches drop out.
  const cleaned = s.replace(/https?:\/\/\S+/g, ' ')
  const matches = cleaned.match(/\S+/g)
  return matches ? matches.length : 0
}

/// Returns the estimated reading time in minutes (rounded up to at least 1)
/// for a post-document block list.
export function readingTimeMinutes(blocks: Block[] | undefined): number {
  return readingStats(blocks).minutes
}

/// Returns both the prose+code word count and the reading-time estimate so
/// callers can render "~N min read · M words" without iterating the blocks
/// twice. The image-caption beat (8 "words" per image) is counted in both
/// — same fudge for both surfaces, no rule that needs to vary.
export function readingStats(blocks: Block[] | undefined): { words: number; minutes: number }
{
  if (!blocks?.length) return { words: 0, minutes: 1 }
  let proseWords = 0
  let codeWords = 0
  for (const b of blocks) {
    switch (b.type) {
      case 'header': proseWords += wordCount(b.data.text); break
      case 'text':   proseWords += wordCount(b.data.markdown); break
      case 'code':   codeWords  += wordCount(b.data.code); break
      case 'image':  proseWords += 8; break // figure caption / "looking-at" beat
    }
  }
  const minutes = (proseWords / PROSE_WPM) + (codeWords / CODE_WPM)
  return {
    words: proseWords + codeWords,
    minutes: Math.max(1, Math.ceil(minutes))
  }
}
