export const TASK_STATUS = {
  DRAFT: 'Draft',
  ISSUED: 'Issued',
  IN_PROGRESS: 'InProgress',
  COMPLETED: 'Completed',
  CLOSED: 'Closed'
}

export const TASK_STATUS_LABELS = {
  [TASK_STATUS.DRAFT]: 'Черновик',
  [TASK_STATUS.ISSUED]: 'Выдано',
  [TASK_STATUS.IN_PROGRESS]: 'В работе',
  [TASK_STATUS.COMPLETED]: 'Выполнено',
  [TASK_STATUS.CLOSED]: 'Закрыто'
}

export const TASK_STATUS_COLORS = {
  [TASK_STATUS.DRAFT]: 'gray',
  [TASK_STATUS.ISSUED]: 'blue',
  [TASK_STATUS.IN_PROGRESS]: 'yellow',
  [TASK_STATUS.COMPLETED]: 'green',
  [TASK_STATUS.CLOSED]: 'gray'
}