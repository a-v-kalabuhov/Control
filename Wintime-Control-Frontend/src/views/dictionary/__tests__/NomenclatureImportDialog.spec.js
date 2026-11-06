// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import NomenclatureImportDialog from '../NomenclatureImportDialog.vue'
import { nomenclatureImportApi } from '@/api/nomenclatureImport'

vi.mock('@/api/nomenclatureImport', () => ({
  nomenclatureImportApi: { upload: vi.fn() }
}))

function mountDialog() {
  return mount(NomenclatureImportDialog, {
    props: { modelValue: true },
    global: { plugins: [ElementPlus] },
    attachTo: document.body
  })
}

describe('NomenclatureImportDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    document.body.innerHTML = ''
  })

  it('чистый файл импортирует сразу и сообщает родителю', async () => {
    nomenclatureImportApi.upload.mockResolvedValue({
      data: { status: 'Imported', importedRows: 2, skippedRows: 0, report: [] }
    })
    const wrapper = mountDialog()
    const file = new File(['xlsx'], 'nomen.xlsx')

    wrapper.vm.onFileSelected({ target: { files: [file] } })
    await wrapper.vm.upload(false)
    await flushPromises()

    expect(nomenclatureImportApi.upload).toHaveBeenCalledWith(file, false)
    expect(wrapper.emitted('imported')).toHaveLength(1)
    expect(document.body.textContent).toContain('Импортировано строк: 2')
    expect(document.body.textContent).not.toContain('Пропустить ошибочные')
    wrapper.unmount()
  })

  it('при ошибках не импортирует, затем повторно отправляет тот же файл с пропуском', async () => {
    nomenclatureImportApi.upload
      .mockResolvedValueOnce({
        data: {
          status: 'ConfirmationRequired', importedRows: 0, skippedRows: 0,
          report: [{ rowNumber: 3, fullArticle: 'A.BAD', blocksRow: true, message: 'Нет названия' }]
        }
      })
      .mockResolvedValueOnce({
        data: { status: 'Imported', importedRows: 1, skippedRows: 1, report: [] }
      })
    const wrapper = mountDialog()
    const file = new File(['xlsx'], 'nomen.xlsx')
    wrapper.vm.onFileSelected({ target: { files: [file] } })

    await wrapper.vm.upload(false)
    await flushPromises()
    expect(wrapper.emitted('imported')).toBeUndefined()
    expect(document.body.textContent).toContain('В БД ничего не изменено')
    expect(document.body.textContent).toContain('Нет названия')

    await wrapper.vm.upload(true)
    await flushPromises()
    expect(nomenclatureImportApi.upload).toHaveBeenNthCalledWith(2, file, true)
    expect(wrapper.emitted('imported')).toHaveLength(1)
    wrapper.unmount()
  })

  it('выбор исправленного файла сбрасывает прежний отчёт', async () => {
    const wrapper = mountDialog()
    wrapper.vm.result = { status: 'ConfirmationRequired', report: [{ message: 'Старая ошибка' }] }
    await wrapper.vm.$nextTick()

    wrapper.vm.onFileSelected({ target: { files: [new File(['new'], 'fixed.xlsx')] } })
    await wrapper.vm.$nextTick()

    expect(wrapper.vm.result).toBeNull()
    expect(wrapper.vm.selectedFile.name).toBe('fixed.xlsx')
    wrapper.unmount()
  })

  it('закрытие сбрасывает файл и отчёт перед следующим открытием', async () => {
    const wrapper = mountDialog()
    wrapper.vm.onFileSelected({ target: { files: [new File(['xlsx'], 'old.xlsx')] } })
    wrapper.vm.result = {
      status: 'ConfirmationRequired',
      report: [{ rowNumber: 2, fullArticle: 'OLD.1', blocksRow: true, message: 'Старая ошибка' }]
    }
    await wrapper.vm.$nextTick()

    await wrapper.setProps({ modelValue: false })
    expect(wrapper.vm.selectedFile).toBeNull()
    expect(wrapper.vm.result).toBeNull()

    await wrapper.setProps({ modelValue: true })
    expect(wrapper.vm.selectedFile).toBeNull()
    expect(wrapper.vm.result).toBeNull()
    expect(document.body.textContent).not.toContain('Старая ошибка')
    wrapper.unmount()
  })
})
