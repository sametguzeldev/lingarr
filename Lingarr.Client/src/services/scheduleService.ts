import { AxiosError, AxiosResponse, AxiosStatic } from 'axios'
import { IScheduleService } from '@/ts'

const service = (http: AxiosStatic, resource = '/api/schedule'): IScheduleService => ({
    remove<T>(jobId: string): Promise<T> {
        return new Promise((resolve, reject) => {
            http.delete(`${resource}/job/remove/${jobId}`)
                .then((response: AxiosResponse<T>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },
    indexShows<T>(): Promise<T> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/job/index/shows`)
                .then((response: AxiosResponse<T>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },
    indexMovies<T>(): Promise<T> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/job/index/movies`)
                .then((response: AxiosResponse<T>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    }
})

export const scheduleService = (axios: AxiosStatic): IScheduleService => {
    return service(axios)
}
