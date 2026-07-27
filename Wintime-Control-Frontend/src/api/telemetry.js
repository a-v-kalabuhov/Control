import apiClient from './client'

export const telemetryApi = {
  // Метаданные сигналов ТПА (для чекбоксов).
  getSignals(id) {
    return apiClient.get(`/imm/${id}/signals`)
  },

  // Агрегированное окно телеметрии. parameters — массив имён сигналов.
  // pointsFrom (опц.) — дельта live: точки телеметрии от этого времени; циклы/статус — за полное окно.
  getDashboard(id, { from, to, parameters, pointsFrom }) {
    return apiClient.get(`/imm/${id}/telemetry-dashboard`, {
      params: { from, to, parameters, ...(pointsFrom ? { pointsFrom } : {}) },
      // ASP.NET List<string> ждёт повтор параметра без индексов: parameters=a&parameters=b
      paramsSerializer: { indexes: null },
    })
  },
}
