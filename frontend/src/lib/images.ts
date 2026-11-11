// One set of client-side image rules for every upload form. Only a quick check of what the browser reports: the
// server decides from the file's actual bytes, and its answer is the one that counts.

export const IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp']
export const MAX_IMAGE_BYTES = 5 * 1024 * 1024

/** An error message, or undefined if the file looks acceptable. */
export function checkImage(file: File): string | undefined {
  if (!IMAGE_TYPES.includes(file.type)) return 'The image must be a JPEG, PNG or WebP file.'
  if (file.size > MAX_IMAGE_BYTES) return 'The image must be at most 5 MB.'
  return undefined
}
