import type { Component } from 'vue'
import HeaderBlockView from '~/components/blocks/HeaderBlockView.vue'
import TextBlockView from '~/components/blocks/TextBlockView.vue'
import ImageBlockView from '~/components/blocks/ImageBlockView.vue'
import CodeBlockView from '~/components/blocks/CodeBlockView.vue'
import HeaderBlockEditor from '~/components/builder/HeaderBlockEditor.vue'
import TextBlockEditor from '~/components/builder/TextBlockEditor.vue'
import ImageBlockEditor from '~/components/builder/ImageBlockEditor.vue'
import CodeBlockEditor from '~/components/builder/CodeBlockEditor.vue'
import type { Block } from '~/types/blocks'

type Entry = { view: Component; editor: Component }

const registry: Record<Block['type'], Entry> = {
  header: { view: HeaderBlockView, editor: HeaderBlockEditor },
  text: { view: TextBlockView, editor: TextBlockEditor },
  image: { view: ImageBlockView, editor: ImageBlockEditor },
  code: { view: CodeBlockView, editor: CodeBlockEditor }
}

export function useBlocks() {
  return registry
}
