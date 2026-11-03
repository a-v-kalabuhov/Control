import apiClient from './client'

export const productTypesApi = {
  getList(params) {
    return apiClient.get('/producttypes', { params })
  },
  getById(id) {
    return apiClient.get(`/producttypes/${id}`)
  },
  create(data) {
    return apiClient.post('/producttypes', data)
  },
  update(id, data) {
    return apiClient.put(`/producttypes/${id}`, data)
  },
  addProduct(typeId, data) {
    return apiClient.post(`/producttypes/${typeId}/products`, data)
  },
  updateProduct(typeId, productId, data) {
    return apiClient.put(`/producttypes/${typeId}/products/${productId}`, data)
  },
  deleteProduct(typeId, productId) {
    return apiClient.delete(`/producttypes/${typeId}/products/${productId}`)
  },
  // BL-36: заменить рецепт изделия целиком (порядок массива = порядок строк рецепта)
  setRecipe(typeId, productId, ingredients) {
    return apiClient.put(`/producttypes/${typeId}/products/${productId}/recipe`, { ingredients })
  }
}
