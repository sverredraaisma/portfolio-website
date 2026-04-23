export type BlockBase<TType extends string, TData> = {
  id: string
  type: TType
  data: TData
}

export type HeaderBlock = BlockBase<'header', { text: string; level: 1 | 2 | 3 }>
export type TextBlock = BlockBase<'text', { markdown: string }>
export type ImageBlock = BlockBase<'image', { src: string; alt: string }>

export type Block = HeaderBlock | TextBlock | ImageBlock

export type PostDocument = {
  blocks: Block[]
}

export const newId = () =>
  globalThis.crypto?.randomUUID?.() ?? Math.random().toString(36).slice(2)
